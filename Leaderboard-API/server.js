// server.js
import express from "express";
import helmet from "helmet";
import cors from "cors";
import rateLimit from "express-rate-limit";
import { MongoClient, ObjectId } from "mongodb";
import "dotenv/config";

const app = express();

// behind render’s proxy so req.ip reflects the real client (needed for rate limit)
app.set("trust proxy", 1);

app.use(
  helmet({
    // allow cross-origin loads for hosted builds (itch, render cdn)
    crossOriginResourcePolicy: { policy: "cross-origin" },
  })
);

// cap payloads to avoid giant bodies
app.use(express.json({ limit: "64kb" }));

// ---- CORS ----
const STATIC_ALLOWED = new Set([
  "http://localhost:5173",
  "http://localhost:3000",
  "http://127.0.0.1:5173",
  "http://127.0.0.1:3000",
]);

// allow dev origins + common itch/render hosts; no-origin = ok for curl/postman
function isAllowedOrigin(origin) {
  if (!origin) return true;
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
  maxAge: 86400, // cache preflights
};

// vary by origin so caches don’t mix responses
app.use((req, res, next) => {
  res.setHeader("Vary", "Origin");
  next();
});
app.use(cors(corsOptions));
app.options("*", cors(corsOptions));

// ---- rate limit ----
// simple per-ip bucket: 60 req/min
const apiLimiter = rateLimit({
  windowMs: 60_000,
  max: 60,
  standardHeaders: true,
});
app.use(apiLimiter);

// ---- mongodb ----
const client = new MongoClient(process.env.MONGODB_URI);
await client.connect();
const db = client.db("game");
const scores = db.collection("scores");
const sessions = db.collection("sessions");

// indexes
await scores.createIndex({ levelId: 1, score: -1, createdAt: 1 }); // leaderboard sort + tie breaker
await scores.createIndex({ sessionId: 1 }, { unique: true, sparse: true }); // idempotency per session
await sessions.createIndex({ expiresAt: 1 }, { expireAfterSeconds: 0 }); // ttl cleanup
await sessions.createIndex({ levelId: 1, used: 1 }); // quick lookups

// ---- helpers ----
function sanitizeName(raw) {
  // trim + collapse spaces; clamp length; default to Anonymous
  let n = String(raw ?? "").trim().replace(/\s+/g, " ");
  if (!n) n = "Anonymous";
  if (n.length > 20) n = n.slice(0, 20);
  return n;
}

// disable caching for dynamic/api responses
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

// start session (single-use token; ttl’d via expiresAt)
app.post("/start-level", async (req, res) => {
  noStore(res);
  const { levelId } = req.body || {};
  if (typeof levelId !== "string") {
    return res.status(400).json({ error: "Bad payload" });
  }

  const now = new Date();
  const expiresAt = new Date(now.getTime() + 30 * 60 * 1000); // 30 min window
  const { insertedId } = await sessions.insertOne({
    levelId,
    startAt: now,
    used: false,
    createdAt: now,
    expiresAt,
  });

  res.json({ sessionId: insertedId.toString() });
});

// finish: store the client’s snapshot score as-is (intentional).
// if clientScore is missing/invalid, reject. session is marked used after a successful insert.
app.post("/finish-level", async (req, res) => {
  noStore(res);
  const {
    levelId,
    sessionId,
    stars,
    name,
    clientDurationSeconds, // optional, analytics only
    clientScore,           // required; this is what gets ranked
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
    // minimal payload validation; stars kept for display/analytics
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

    // require a finite, sane score (0 allowed)
    if (!isFiniteNumber(clientScore) || clientScore < 0 || clientScore >= 1e9) {
      return res.status(400).json({ error: "Missing or invalid clientScore" });
    }

    const _id = new ObjectId(sessionId);

    // find session by id + level (don’t require used:false; idempotency handled by unique index)
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

    // clamp/accept duration to a reasonable window
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
        score: Number(clientScore),            // leaderboard value (snapshot)
        scoreSource: "client",
        duration: durationStored,              // convenience/analytics
        clientDurationSeconds: durationStored, // keep raw key too
        clientScore: Number(clientScore),      // mirror
        createdAt: now,
      });
    } catch (e) {
      if (e?.code === 11000) {
        // duplicate session submit → return the existing score
        const prev = await scores.findOne(
          { sessionId: _id },
          { projection: { _id: 0, score: 1 } }
        );
        if (prev) {
          // mark session used (best effort)
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

    // flip the session to used after a successful insert
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

// leaderboard (highest first; older wins on ties)
app.get("/leaderboard/:levelId", async (req, res) => {
  noStore(res);
  const levelId = req.params.levelId;
  const list = await scores
    .find({ levelId }, { projection: { _id: 0, name: 1, score: 1 } })
    .sort({ score: -1, createdAt: 1 })
    .limit(10000) // generous cap
    .toArray();
  res.json(list);
});

const port = process.env.PORT || 3000;
app.listen(port, () => console.log(`API listening on :${port}`));
