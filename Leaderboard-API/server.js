// server.js
import express from "express";
import helmet from "helmet";
import cors from "cors";
import rateLimit from "express-rate-limit";
import { MongoClient, ObjectId } from "mongodb";
import "dotenv/config";

const app = express();

// Behind Render's proxy so req.ip works for rate limiting
app.set("trust proxy", 1);

app.use(
  helmet({
    crossOriginResourcePolicy: { policy: "cross-origin" },
  })
);
app.use(express.json({ limit: "64kb" }));

// ---- CORS ----
const STATIC_ALLOWED = new Set([
  "http://localhost:5173",
  "http://localhost:3000",
  "http://127.0.0.1:5173",
  "http://127.0.0.1:3000",
]);

function isAllowedOrigin(origin) {
  if (!origin) return true; // allow curl/postman/no-origin
  if (STATIC_ALLOWED.has(origin)) return true;
  try {
    const host = new URL(origin).host;
    return (
      host.endsWith(".onrender.com") ||
      host.endsWith(".itch.io") ||
      host.endsWith(".hwcdn.net") ||
      host.endsWith(".itch.zone") ||
      host === "itch.zone" ||
      host === "html.itch.zone" ||
      host === "html-classic.itch.zone"
    );
  } catch {
    return false;
  }
}

const corsOptions = {
  origin(origin, cb) {
    if (isAllowedOrigin(origin)) return cb(null, true);
    cb(new Error("Not allowed by CORS"));
  },
  methods: ["GET", "POST", "OPTIONS"],
  allowedHeaders: ["Content-Type", "Accept"],
  maxAge: 86400,
};

app.use((req, res, next) => {
  res.setHeader("Vary", "Origin");
  next();
});
app.use(cors(corsOptions));
// Explicitly handle preflight anywhere:
app.options("*", cors(corsOptions));

// ---- Rate limit ----
const apiLimiter = rateLimit({
  windowMs: 60_000,
  max: 60,
  standardHeaders: true,
});
app.use(apiLimiter);

// ---- MongoDB ----
const client = new MongoClient(process.env.MONGODB_URI);
await client.connect();
const db = client.db("game");
const scores = db.collection("scores");
const sessions = db.collection("sessions");

// Indexes
await scores.createIndex({ levelId: 1, score: -1, createdAt: 1 });
await scores.createIndex({ sessionId: 1 }, { unique: true, sparse: true }); // idempotency
await sessions.createIndex({ expiresAt: 1 }, { expireAfterSeconds: 0 });
await sessions.createIndex({ levelId: 1, used: 1 });

// ---- helpers ----
function computeFinalScore(duration, stars) {
  if (!isFinite(duration) || duration <= 0) return 0;
  const s = Math.max(0, Math.min(9999, Number(stars) || 0));
  return (1000 / duration) * Math.sqrt(s);
}
function sanitizeName(raw) {
  let n = String(raw ?? "").trim().replace(/\s+/g, " ");
  if (!n) n = "Anonymous";
  if (n.length > 20) n = n.slice(0, 20);
  return n;
}

// Small helper to prevent caching dynamic responses
function noStore(res) {
  res.set("Cache-Control", "no-store");
}

// ---- routes ----
app.get("/", (_req, res) => {
  noStore(res);
  res.json({ ok: true, message: "Game API is running." });
});
app.get("/healthz", (_req, res) => res.sendStatus(200));

// Start session 
app.post("/start-level", async (req, res) => {
  noStore(res);
  const { levelId } = req.body || {};
  if (typeof levelId !== "string") {
    return res.status(400).json({ error: "Bad payload" });
  }

  const now = new Date();
  const expiresAt = new Date(now.getTime() + 30 * 60 * 1000);
  const { insertedId } = await sessions.insertOne({
    levelId,
    startAt: now,
    used: false,
    createdAt: now,
    expiresAt,
  });
  res.json({ sessionId: insertedId.toString() });
});

// Finish (uses client-frozen duration if plausible; atomic session consume; idempotent)
app.post("/finish-level", async (req, res) => {
  noStore(res);
  try {
    const {
      levelId,
      sessionId,
      stars,
      name,
      clientDurationSeconds, // optional, from client
    } = req.body || {};

    console.log("finish-level body", {
      levelId,
      sessionId,
      stars,
      name,
      clientDurationSeconds,
    });

    if (
      typeof levelId !== "string" ||
      typeof sessionId !== "string" ||
      !Number.isInteger(stars)
    ) {
      return res.status(400).json({ error: "Bad payload" });
    }
    if (!ObjectId.isValid(sessionId)) {
      return res.status(400).json({ error: "Invalid session id" });
    }

    const now = new Date();

    // Atomically mark the session as used and read its previous data
    const { value: sess } = await sessions.findOneAndUpdate(
      { _id: new ObjectId(sessionId), levelId, used: false },
      { $set: { used: true, finishedAt: now } },
      { returnDocument: "before" }
    );

    if (!sess) {
      // Either invalid session OR already used.
      // If already used, return the previously computed score (idempotency).
      const prev = await scores.findOne(
        { sessionId: new ObjectId(sessionId) },
        { projection: { _id: 0, score: 1 } }
      );

      if (prev) {
        return res.json({ ok: true, score: prev.score });
      }

      // For debugging, see what exists
      const dbg = await sessions.findOne({ _id: new ObjectId(sessionId) });
      console.warn("finish-level session lookup", {
        found: !!dbg,
        used: dbg?.used,
        levelIdOnSession: dbg?.levelId,
      });

      return res.status(400).json({ error: "Invalid or used session" });
    }

    const serverDuration = Math.max(0, (now - new Date(sess.startAt)) / 1000);

    // Prefer client frozen duration if it's sensible and close to server's clock
    let duration = serverDuration;
    if (
      Number.isFinite(clientDurationSeconds) &&
      clientDurationSeconds > 0 &&
      clientDurationSeconds < 60 * 60 // < 1h sanity bound
    ) {
      const diff = Math.abs(clientDurationSeconds - serverDuration);
      if (diff <= 5 || serverDuration < 1) {
        duration = clientDurationSeconds;
      }
    }

    const cleanName = sanitizeName(name);
    const score = computeFinalScore(duration, stars);

    await scores.insertOne({
      sessionId: new ObjectId(sessionId), // for idempotency
      levelId,
      stars,
      name: cleanName,
      score,
      duration, // chosen duration
      serverDuration, // for debugging/analytics
      clientDurationSeconds: Number.isFinite(clientDurationSeconds)
        ? clientDurationSeconds
        : null,
      createdAt: now,
    });

    res.json({ ok: true, score });
  } catch (e) {
    // Handle duplicate insert (session already has a score) gracefully
    if (e?.code === 11000) {
      const sid = req?.body?.sessionId && ObjectId.isValid(req.body.sessionId)
        ? new ObjectId(req.body.sessionId)
        : null;
      if (sid) {
        const prev = await scores.findOne(
          { sessionId: sid },
          { projection: { _id: 0, score: 1 } }
        );
        if (prev) return res.json({ ok: true, score: prev.score });
      }
    }
    console.error(e);
    res.status(500).json({ error: "Server error" });
  }
});

// Leaderboard
app.get("/leaderboard/:levelId", async (req, res) => {
  noStore(res);
  const levelId = req.params.levelId;
  const list = await scores
    .find({ levelId }, { projection: { _id: 0, name: 1, score: 1 } })
    .sort({ score: -1, createdAt: 1 })
    .limit(10000)
    .toArray();
  res.json(list);
});

const port = process.env.PORT || 3000;
app.listen(port, () => console.log(`API listening on :${port}`));
