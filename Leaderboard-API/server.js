import express from "express";
import helmet from "helmet";
import cors from "cors";
import rateLimit from "express-rate-limit";
import { MongoClient, ObjectId } from "mongodb";
import "dotenv/config";

const app = express();
app.use(helmet());
app.use(express.json({ limit: "32kb" }));

app.use(cors({
  origin: ["http://localhost:5173", "http://localhost:3000"], // add your game host(s) here
  methods: ["GET","POST"]
}));

const apiLimiter = rateLimit({ windowMs: 60_000, max: 60, standardHeaders: true });
app.use(apiLimiter);

// ---- MongoDB ----
const client = new MongoClient(process.env.MONGODB_URI);
await client.connect();
const db = client.db("game");
const scores = db.collection("scores");
const sessions = db.collection("sessions");

// Indexes
await scores.createIndex({ score: -1, createdAt: 1 });
await sessions.createIndex({ expiresAt: 1 }, { expireAfterSeconds: 0 });
await sessions.createIndex({ playerId: 1, levelId: 1, used: 1 });

// ---- helpers ----
function computeFinalScore(duration, stars) {
  if (!isFinite(duration) || duration <= 0) return 0;
  const s = Math.max(0, Math.min(9999, Number(stars) || 0));
  return (1000 / duration) * Math.sqrt(s);
}

function sanitizeName(raw) {
  let n = String(raw ?? "").trim().replace(/\s+/g, " ");
  if (!n) n = "Anonymous";
  if (n.length > 20) n = n.slice(0, 20);  // <- only enforced rule
  return n;
}

// ---- routes ----

// Start: issue single-use session
app.post("/start-level", async (req, res) => {
  const { playerId, levelId } = req.body || {};
  if (typeof playerId !== "string" || typeof levelId !== "string")
    return res.status(400).json({ error: "Bad payload" });

  const now = new Date();
  const expiresAt = new Date(now.getTime() + 30 * 60 * 1000);
  const { insertedId } = await sessions.insertOne({
    playerId, levelId, startAt: now, used: false, createdAt: now, expiresAt
  });
  res.json({ sessionId: insertedId.toString() });
});

// Finish: compute score server-side + store `name`
app.post("/finish-level", async (req, res) => {
  try {
    const { playerId, levelId, sessionId, stars, name } = req.body || {};
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

    const cleanName = sanitizeName(name);
    const score = computeFinalScore(duration, stars);

    await sessions.updateOne({ _id: sess._id, used: false }, { $set: { used: true, finishedAt: now } });

    await scores.insertOne({ playerId, levelId, duration, stars, score, name: cleanName, createdAt: now });

    res.json({ ok: true, score });
  } catch (e) {
    console.error(e);
    res.status(500).json({ error: "Server error" });
  }
});

// Leaderboard: return just name + score
app.get("/leaderboard/:levelId", async (req, res) => {
  const levelId = req.params.levelId;
  const list = await scores
    .find({ levelId }, { projection: { _id: 0, name: 1, score: 1 } })
    .sort({ score: -1, createdAt: 1 })
    .limit(10000)
    .toArray();
  res.json(list);
});

const port = process.env.PORT || 3000;
app.listen(port, () => console.log(`API listening on http://localhost:${port}`));
