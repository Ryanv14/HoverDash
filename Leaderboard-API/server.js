import express from "express";
import helmet from "helmet";
import cors from "cors";
import rateLimit from "express-rate-limit";
import { MongoClient, ObjectId } from "mongodb";
import "dotenv/config";

const app = express();
app.use(helmet());
app.use(express.json({ limit: "32kb" }));

// CORS: list the origins that will call your API (add your WebGL host later)
app.use(cors({
  origin: ["http://localhost:5173", "http://localhost:3000"], // add your game site URL here
  methods: ["GET","POST"]
}));

// Basic rate limiting
const apiLimiter = rateLimit({ windowMs: 60_000, max: 60, standardHeaders: true });
app.use(apiLimiter);

// ---- MongoDB setup ----
const client = new MongoClient(process.env.MONGODB_URI);
await client.connect();
const db = client.db("game");
const scores = db.collection("scores");
const sessions = db.collection("sessions");

// Indexes (created at startup if missing)
await scores.createIndex({ score: -1, createdAt: 1 });
await scores.createIndex({ playerId: 1, score: -1 });
await sessions.createIndex({ expiresAt: 1 }, { expireAfterSeconds: 0 });
await sessions.createIndex({ playerId: 1, levelId: 1, used: 1 });

// Optional: collection validation (safe defaults)
try {
  await db.command({
    collMod: "scores",
    validator: {
      $jsonSchema: {
        bsonType: "object",
        required: ["playerId","levelId","duration","stars","score","createdAt"],
        properties: {
          playerId: { bsonType: "string", minLength: 8, maxLength: 64 },
          levelId:  { bsonType: "string", minLength: 1, maxLength: 64 },
          duration: { bsonType: "double", minimum: 0.5, maximum: 7200 },
          stars:    { bsonType: "int", minimum: 0, maximum: 9999 },
          score:    { bsonType: "double", minimum: 0 },
          createdAt:{ bsonType: "date" }
        },
        additionalProperties: false
      }
    }
  });
} catch { /* ignore if first create or unsupported */ }

// ---- Helpers ----
function computeFinalScore(duration, stars) {
  if (!isFinite(duration) || duration <= 0) return 0;
  stars = Math.max(0, Math.min(9999, Number(stars) || 0));
  return (1000 / duration) * Math.sqrt(stars);
}

// ---- Routes ----

// 1) Start level: issue a single-use session with server-side start time
app.post("/start-level", async (req, res) => {
  const { playerId, levelId } = req.body || {};
  if (typeof playerId !== "string" || typeof levelId !== "string")
    return res.status(400).json({ error: "Bad payload" });

  const now = new Date();
  const expiresAt = new Date(now.getTime() + 30 * 60 * 1000); // 30 minutes
  const { insertedId } = await sessions.insertOne({
    playerId, levelId, startAt: now, used: false, createdAt: now, expiresAt
  });
  res.json({ sessionId: insertedId.toString() });
});

// 2) Finish level: compute duration & score server-side, store result
app.post("/finish-level", async (req, res) => {
  try {
    const { playerId, levelId, sessionId, stars } = req.body || {};
    if (typeof playerId !== "string" || typeof levelId !== "string" ||
        typeof sessionId !== "string" || !Number.isInteger(stars)) {
      return res.status(400).json({ error: "Bad payload" });
    }

    const sess = await sessions.findOne({
      _id: new ObjectId(sessionId), playerId, levelId, used: false
    });
    if (!sess) return res.status(400).json({ error: "Invalid or used session" });

    const now = new Date();
    const duration = Math.max(0, (now - new Date(sess.startAt)) / 1000);

    if (duration < 1 || duration > 7200) return res.status(400).json({ error: "Unreasonable duration" });
    if (stars < 0 || stars > 9999) return res.status(400).json({ error: "Bad stars" });

    const score = computeFinalScore(duration, stars);

    await sessions.updateOne({ _id: sess._id, used: false }, { $set: { used: true, finishedAt: now } });

    await scores.insertOne({ playerId, levelId, duration, stars, score, createdAt: now });

    res.json({ ok: true, score });
  } catch (e) {
    console.error(e);
    res.status(500).json({ error: "Server error" });
  }
});

// 3) Top 10,000 for a level
app.get("/leaderboard/:levelId", async (req, res) => {
  const levelId = req.params.levelId;
  const list = await scores
    .find({ levelId }, { projection: { _id: 0 } })
    .sort({ score: -1, createdAt: 1 })
    .limit(10000)
    .toArray();
  res.json(list);
});

const port = process.env.PORT || 3000;
app.listen(port, () => console.log(`API listening on http://localhost:${port}`));
