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
function sanitizeName(raw) {
  let n = String(raw ?? "").trim().replace(/\s+/g, " ");
  if (!n) n = "Anonymous";
  if (n.length > 20) n = n.slice(0, 20);
  return n;
}
function noStore(res) {
  res.set("Cache-Control", "no-store");
}
function isFiniteNumber(n) {
  return typeof n === "number" && Number.isFinite(n);
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

// Finish: ONLY accept client snapshot score. Never compute on server.
// If clientScore missing/invalid => reject (no insert, no "used" mark).
app.post("/finish-level", async (req, res) => {
  noStore(res);
  const {
    levelId,
    sessionId,
    stars,
    name,
    clientDurationSeconds, // optional; stored for analytics only
    clientScore,           // REQUIRED: this is the only score we will store
  } = req.body || {};

  console.log("finish-level body", {
    levelId,
    sessionId,
    stars,
    name,
    clientDurationSeconds,
    clientScore,
  });

  try {
    // Validate payload (stars still required so you can store it with the score doc)
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

    // Require a finite clientScore (>= 0 allowed; if you want to forbid 0, change to > 0)
    if (!isFiniteNumber(clientScore) || clientScore < 0 || clientScore >= 1e9) {
      return res.status(400).json({ error: "Missing or invalid clientScore" });
    }

    const _id = new ObjectId(sessionId);

    // Look up session (do NOT require used:false); reject if not found / wrong level
    const sess = await sessions.findOne({ _id, levelId });
    if (!sess) {
      const dbg = await sessions.findOne({ _id });
      console.warn("finish-level session lookup", {
        found: !!dbg,
        used: dbg?.used,
        levelIdOnSession: dbg?.levelId,
        providedLevelId: levelId,
      });
      return res.status(400).json({ error: "Invalid or used session" });
    }

    const now = new Date();

    // Store the snapshot score exactly as provided
    const durationStored =
      isFiniteNumber(clientDurationSeconds) && clientDurationSeconds >= 0 && clientDurationSeconds < 60 * 60
        ? clientDurationSeconds
        : null;

    try {
      await scores.insertOne({
        sessionId: _id,
        levelId,
        stars,
        name: sanitizeName(name),
        score: Number(clientScore),            // ONLY the client snapshot counts
        scoreSource: "client",
        duration: durationStored,              // for analytics/UX
        clientDurationSeconds: durationStored, // duplicate for clarity
        clientScore: Number(clientScore),      // store raw snapshot too
        createdAt: now,
      });
    } catch (e) {
      if (e?.code === 11000) {
        // Duplicate — the score for this session already exists. Return it.
        const prev = await scores.findOne(
          { sessionId: _id },
          { projection: { _id: 0, score: 1 } }
        );
        if (prev) {
          // Best-effort mark session used
          await sessions.updateOne(
            { _id },
            { $set: { used: true, finishedAt: now } }
          );
          return res.json({ ok: true, score: prev.score, scoreSource: "existing" });
        }
        throw e;
      }
      throw e;
    }

    // Mark session used only after successful insert
    await sessions.updateOne(
      { _id },
      { $set: { used: true, finishedAt: now } }
    );

    console.log("finish-level stored", {
      sessionId,
      levelId,
      stars,
      clientDurationSeconds: durationStored,
      clientScore,
    });

    res.json({ ok: true, score: Number(clientScore), scoreSource: "client" });
  } catch (e) {
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
