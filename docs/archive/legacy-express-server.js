import express from 'express';
import path from 'path';
import fs from 'fs';
import { createServer as createViteServer } from 'vite';
import { GoogleGenAI, Type } from '@google/genai';
import crypto from 'crypto';
import dotenv from 'dotenv';

dotenv.config();

const app = express();
const PORT = 3000;

// Set up payload limits higher to support high-resolution base64 image uploads
app.use(express.json({ limit: '50mb' }));
app.use(express.urlencoded({ limit: '50mb', extended: true }));

// Ensure database directories exist
const DATA_DIR = path.join(process.cwd(), 'data');
const UPLOADS_DIR = path.join(DATA_DIR, 'uploads');
const USERS_FILE = path.join(DATA_DIR, 'users.json');
const BOARDS_FILE = path.join(DATA_DIR, 'boards.json');
const IMAGES_FILE = path.join(DATA_DIR, 'images.json');

if (!fs.existsSync(DATA_DIR)) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
}
if (!fs.existsSync(UPLOADS_DIR)) {
  fs.mkdirSync(UPLOADS_DIR, { recursive: true });
}
if (!fs.existsSync(USERS_FILE)) {
  fs.writeFileSync(USERS_FILE, JSON.stringify([], null, 2));
}
if (!fs.existsSync(BOARDS_FILE)) {
  fs.writeFileSync(BOARDS_FILE, JSON.stringify([], null, 2));
}
if (!fs.existsSync(IMAGES_FILE)) {
  fs.writeFileSync(IMAGES_FILE, JSON.stringify({}, null, 2));
}

// Serve uploaded assets
app.use('/data/uploads', express.static(UPLOADS_DIR));

// Helper: Read/Write Database
function readUsers() {
  try {
    return JSON.parse(fs.readFileSync(USERS_FILE, 'utf-8'));
  } catch (e) {
    return [];
  }
}

function writeUsers(users) {
  fs.writeFileSync(USERS_FILE, JSON.stringify(users, null, 2));
}

function readBoards() {
  try {
    return JSON.parse(fs.readFileSync(BOARDS_FILE, 'utf-8'));
  } catch (e) {
    return [];
  }
}

function writeBoards(boards) {
  fs.writeFileSync(BOARDS_FILE, JSON.stringify(boards, null, 2));
}

function readImages() {
  try {
    return JSON.parse(fs.readFileSync(IMAGES_FILE, 'utf-8'));
  } catch (e) {
    return {};
  }
}

function writeImages(images) {
  fs.writeFileSync(IMAGES_FILE, JSON.stringify(images, null, 2));
}


// Security: Native PBKDF2 Hashing
function hashPassword(password, salt) {
  return crypto.pbkdf2Sync(password, salt, 1000, 64, 'sha512').toString('hex');
}

// Middleware: Bearer Authentication
function authenticate(req, res, next) {
  const authHeader = req.headers.authorization;
  if (!authHeader || !authHeader.startsWith('Bearer ')) {
    return res.status(401).json({ error: 'Unauthorized: Missing token header.' });
  }
  const token = authHeader.substring(7);
  const users = readUsers();
  const user = users.find(u => u.id === token); // Simple highly reliable token (UserID = Token)
  if (!user) {
    return res.status(401).json({ error: 'Unauthorized: Invalid token session.' });
  }
  req.user = { id: user.id, email: user.email, name: user.name, preferences: user.preferences };
  next();
}

// --- API ENDPOINTS ---

// Register User
app.post('/api/auth/register', (req, res) => {
  const { email, password, name } = req.body;
  if (!email || !password || !name) {
    return res.status(400).json({ error: 'Please provide email, password, and name.' });
  }

  const users = readUsers();
  if (users.some(u => u.email.toLowerCase() === email.toLowerCase())) {
    return res.status(400).json({ error: 'An account with this email already exists.' });
  }

  const salt = crypto.randomBytes(16).toString('hex');
  const passwordHash = hashPassword(password, salt);
  const id = crypto.randomUUID();

  const newUser = {
    id,
    email: email.toLowerCase(),
    name,
    preferences: {
      darkMode: false,
      notificationsEnabled: true,
      highContrast: false
    },
    passwordHash,
    salt
  };

  users.push(newUser);
  writeUsers(users);

  // Initialize a default board for the new user so they start inspired!
  const boards = readBoards();
  const defaultBoard = {
    id: crypto.randomUUID(),
    title: 'My Dream Journey 🪐',
    description: 'Welcome to your Vision Board! Arrange files, quotes, notes, and dreams. Move elements freely.',
    category: 'Personal Development',
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ownerId: id,
    isShared: false,
    collaborators: [],
    items: [
      {
        id: crypto.randomUUID(),
        type: 'quote',
        title: 'Daily Inspiration',
        content: 'The future belongs to those who believe in the beauty of their dreams. ✨',
        color: 'bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800',
        x: 10,
        y: 10,
        width: 32,
        height: 20,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      },
      {
        id: crypto.randomUUID(),
        type: 'note',
        title: 'Action Point 🚀',
        content: '- Read 1 chapter today\n- Stretch for 10 minutes\n- Drink 3L of water',
        color: 'bg-amber-50 dark:bg-amber-950 border-amber-200 dark:border-amber-800',
        x: 45,
        y: 15,
        width: 25,
        height: 28,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      },
      {
        id: crypto.randomUUID(),
        type: 'image',
        title: 'Morning Serenity',
        content: 'https://images.unsplash.com/photo-1506126613408-eca07ce68773?w=1000',
        caption: 'Focusing on peace, meditation, and healthy daily habits.',
        x: 20,
        y: 45,
        width: 35,
        height: 40,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      }
    ]
  };
  boards.push(defaultBoard);
  writeBoards(boards);

  res.json({
    token: id,
    user: { id, email: newUser.email, name: newUser.name, preferences: newUser.preferences }
  });
});

// Login User
app.post('/api/auth/login', (req, res) => {
  const { email, password } = req.body;
  if (!email || !password) {
    return res.status(400).json({ error: 'Please submit email and password.' });
  }

  const users = readUsers();
  const user = users.find(u => u.email.toLowerCase() === email.toLowerCase());
  if (!user) {
    return res.status(400).json({ error: 'Invalid email or password.' });
  }

  const checkedHash = hashPassword(password, user.salt);
  if (checkedHash !== user.passwordHash) {
    return res.status(400).json({ error: 'Invalid email or password.' });
  }

  res.json({
    token: user.id,
    user: { id: user.id, email: user.email, name: user.name, preferences: user.preferences }
  });
});

// Save user preferences
app.post('/api/auth/preferences', authenticate, (req, res) => {
  const users = readUsers();
  const index = users.findIndex(u => u.id === req.user.id);
  if (index !== -1) {
    users[index].preferences = {
      ...users[index].preferences,
      ...req.body
    };
    writeUsers(users);
    res.json({ success: true, preferences: users[index].preferences });
  } else {
    res.status(404).json({ error: 'User session not found.' });
  }
});

// Get Boards (including shared with me)
app.get('/api/boards', authenticate, (req, res) => {
  const boards = readBoards();
  const userEmail = req.user.email.toLowerCase();
  const userBoards = boards.filter(b => b.ownerId === req.user.id || b.collaborators.some(c => c.toLowerCase() === userEmail));
  res.json(userBoards);
});

// Create Board
app.post('/api/boards', authenticate, (req, res) => {
  const { title, description, category, isShared, collaborators } = req.body;
  if (!title) {
    return res.status(400).json({ error: 'Board title is required.' });
  }

  const boards = readBoards();
  const newBoard = {
    id: crypto.randomUUID(),
    title,
    description: description || '',
    category: category || 'Personal',
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ownerId: req.user.id,
    isShared: isShared || false,
    collaborators: collaborators || [],
    items: []
  };

  boards.push(newBoard);
  writeBoards(boards);
  res.json(newBoard);
});

// Fetch Single Board
app.get('/api/boards/:id', authenticate, (req, res) => {
  const boards = readBoards();
  const boardId = req.params.id;
  const board = boards.find(b => b.id === boardId);

  if (!board) {
    return res.status(404).json({ error: 'Board not found.' });
  }

  // Authorization check
  const userEmail = req.user.email.toLowerCase();
  const isOwner = board.ownerId === req.user.id;
  const isCollaborator = board.collaborators.some(c => c.toLowerCase() === userEmail);
  if (!isOwner && !isCollaborator) {
    return res.status(403).json({ error: 'Forbidden: You do not have access to this board.' });
  }

  res.json(board);
});

// Update Full Board
app.put('/api/boards/:id', authenticate, (req, res) => {
  const boards = readBoards();
  const boardId = req.params.id;
  const index = boards.findIndex(b => b.id === boardId);

  if (index === -1) {
    return res.status(404).json({ error: 'Board not found.' });
  }

  const board = boards[index];
  const userEmail = req.user.email.toLowerCase();
  const isOwner = board.ownerId === req.user.id;
  const isCollaborator = board.collaborators.some(c => c.toLowerCase() === userEmail);
  if (!isOwner && !isCollaborator) {
    return res.status(403).json({ error: 'Forbidden: You cannot modify this board.' });
  }

  // Preserve core fields
  const updatedBoard = {
    ...board,
    title: req.body.title ?? board.title,
    description: req.body.description ?? board.description,
    category: req.body.category ?? board.category,
    isShared: req.body.isShared ?? board.isShared,
    collaborators: req.body.collaborators ?? board.collaborators,
    items: req.body.items ?? board.items,
    updatedAt: new Date().toISOString()
  };

  boards[index] = updatedBoard;
  writeBoards(boards);
  res.json(updatedBoard);
});

// Delete Board
app.delete('/api/boards/:id', authenticate, (req, res) => {
  const boards = readBoards();
  const boardId = req.params.id;
  const index = boards.findIndex(b => b.id === boardId);

  if (index === -1) {
    return res.status(404).json({ error: 'Board not found.' });
  }

  if (boards[index].ownerId !== req.user.id) {
    return res.status(403).json({ error: 'Forbidden: Only the owner can delete the board.' });
  }

  boards.splice(index, 1);
  writeBoards(boards);
  res.json({ success: true, message: 'Board successfully deleted.' });
});

// Image Upload Endpoint (saves base64 to DB and returns dynamic URL)
app.post('/api/upload', authenticate, (req, res) => {
  try {
    const { base64Data, mimeType, fileName } = req.body;
    if (!base64Data) {
      return res.status(400).json({ error: 'No image data submitted.' });
    }

    // Clean base64 strip
    const pureBase64 = base64Data.replace(/^data:image\/\w+;base64,/, '');
    const buffer = Buffer.from(pureBase64, 'base64');

    // Validate standard sizing
    if (buffer.length > 15 * 1024 * 1024) { // 15MB max limit check
      return res.status(400).json({ error: 'Upload exceeds 15MB limit.' });
    }

    // Resolve mime type
    let resolvedMimeType = mimeType || 'image/png';
    const match = base64Data.match(/^data:(image\/\w+);base64,/);
    if (match && match[1]) {
      resolvedMimeType = match[1];
    }

    const id = crypto.randomUUID();
    const images = readImages();
    images[id] = {
      base64Data: pureBase64,
      mimeType: resolvedMimeType,
      createdAt: new Date().toISOString()
    };
    writeImages(images);

    const downloadUrl = `/api/images/${id}`;
    res.json({ url: downloadUrl });
  } catch (err) {
    console.error('File Upload Error:', err);
    res.status(500).json({ error: 'Internal system failed to serialize image.' });
  }
});

// Serve image assets lazily from memory/DB mockup
app.get('/api/images/:id', (req, res) => {
  try {
    const id = req.params.id;
    const images = readImages();
    const img = images[id];

    if (!img) {
      return res.status(404).json({ error: 'Image not found.' });
    }

    const buffer = Buffer.from(img.base64Data, 'base64');
    res.setHeader('Content-Type', img.mimeType);
    res.send(buffer);
  } catch (err) {
    console.error('Get Image Error:', err);
    res.status(500).json({ error: 'Internal system failed to retrieve image.' });
  }
});

// Cloud Sync Engine with Conflict-Resolution (Version & Timestamp Checks)
app.post('/api/sync', authenticate, (req, res) => {
  try {
    const { queue, clientTimestamp } = req.body;
    if (!Array.isArray(queue)) {
      return res.status(400).json({ error: 'Sync requires a list queue of actions.' });
    }

    const boards = readBoards();
    const userEmail = (req.user.email || '').toLowerCase();

    // Sort queue by action sequence to ensure sequential accuracy
    const sortedQueue = [...queue].sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime());

    for (const item of sortedQueue) {
      const { action, boardId, itemId, payload, timestamp } = item;
      const boardIndex = boards.findIndex(b => b.id === boardId);

      // Guard update permissions
      if (boardIndex !== -1) {
        const b = boards[boardIndex];
        const isOwner = b.ownerId === req.user.id;
        const isCollab = b.collaborators.some(c => c.toLowerCase() === userEmail);
        if (!isOwner && !isCollab) continue; // skip unauthorised syncing
      }

      if (action === 'create') {
        if (boardIndex === -1) {
          const newBoard = {
            ...payload,
            ownerId: req.user.id,
            updatedAt: timestamp || new Date().toISOString()
          };
          boards.push(newBoard);
        }
      } else if (action === 'update') {
        if (boardIndex !== -1) {
          const serverBoard = boards[boardIndex];
          // Only update if client action is more recent than baseline server save
          if (new Date(timestamp).getTime() > new Date(serverBoard.updatedAt).getTime()) {
            boards[boardIndex] = {
              ...serverBoard,
              ...payload,
              updatedAt: timestamp
            };
          }
        }
      } else if (action === 'delete') {
        if (boardIndex !== -1 && boards[boardIndex].ownerId === req.user.id) {
          boards.splice(boardIndex, 1);
        }
      } else if (action === 'upsert_item') {
        if (boardIndex !== -1) {
          const serverBoard = boards[boardIndex];
          const itemIndex = serverBoard.items.findIndex(ele => ele.id === itemId);

          let shouldApply = true;
          if (itemIndex !== -1) {
            const serverItem = serverBoard.items[itemIndex];
            // Conflict checking
            if (new Date(timestamp).getTime() < new Date(serverItem.updatedAt).getTime()) {
              shouldApply = false;
            }
          }

          if (shouldApply) {
            const updatedItems = [...serverBoard.items];
            const freshItem = {
              ...payload,
              updatedAt: timestamp || new Date().toISOString()
            };

            if (itemIndex === -1) {
              updatedItems.push(freshItem);
            } else {
              updatedItems[itemIndex] = freshItem;
            }

            serverBoard.items = updatedItems;
            serverBoard.updatedAt = new Date().toISOString();
          }
        }
      } else if (action === 'delete_item') {
        if (boardIndex !== -1) {
          const serverBoard = boards[boardIndex];
          serverBoard.items = serverBoard.items.filter(ele => ele.id !== itemId);
          serverBoard.updatedAt = new Date().toISOString();
        }
      }
    }

    writeBoards(boards);

    // Return full, resolved board registry to update local store completely
    const updatedBoards = boards.filter(b => b.ownerId === req.user.id || b.collaborators.some(c => (c || '').toLowerCase() === userEmail));
    res.json({
      success: true,
      boards: updatedBoards,
      timestamp: new Date().toISOString()
    });
  } catch (err) {
    console.error('Sync endpoint failed:', err);
    res.status(500).json({ error: 'Failed to process database synchronization: ' + (err.message || err.toString()) });
  }
});

// Board AI Suggestions: Based on current board items and themes
app.post('/api/board/recommendations', authenticate, async (req, res) => {
  const { title, description, category, items } = req.body;

  if (!title) {
    return res.status(400).json({ error: 'Board title is required for recommendations.' });
  }

  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey || apiKey === 'MY_GEMINI_API_KEY') {
    return res.json({
      analysis: `We analyzed your board "${title}". It looks like you're setting up some wonderful intentions! Set up your Gemini API Key in Settings to get bespoke structured design grids and quotes. Here are universal starting options to expand your vision:`,
      suggestedColorPalette: ['#6366f1', '#10b981', '#f59e0b', '#ec4899'],
      recommendedItems: [
        {
          type: 'quote',
          title: 'Daily Action Catalyst',
          content: 'Continuous improvement is better than delayed perfection. Expand your board with clear milestones! 🌟',
          color: 'bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800',
          width: 25,
          height: 25
        },
        {
          type: 'note',
          title: 'Growth Intentions Checklist',
          content: '1. Identify one micro-habit you can start today\n2. Block out 15 minutes in your calendar\n3. Share this board with a collaborator',
          color: 'bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800',
          width: 25,
          height: 30
        },
        {
          type: 'image',
          title: 'Focus Mindset Symbol',
          content: 'https://images.unsplash.com/photo-1519681393784-d120267933ba?w=800',
          caption: 'Visualizing structured growth and personal development.',
          width: 30,
          height: 35
        }
      ]
    });
  }

  try {
    const ai = new GoogleGenAI({
      apiKey,
      httpOptions: {
        headers: {
          'User-Agent': 'aistudio-build',
        }
      }
    });

    const itemsContext = (items || []).map(it => `- [${it.type.toUpperCase()}] "${it.title}": ${it.content} ${it.caption ? `(Caption: ${it.caption})` : ''}`).join('\n');

    const promptText = `You are an expert personal alignment coach, visual consultant, and aesthetic coordinator.
You will analyze the current elements on a user's Digital Vision Board to recommend new, highly relevant inspirational content (quotes, actionable tasks, color palettes, and image search concepts) to help the user expand their vision.

Board Context:
- Title: "${title}"
- Description: "${description || 'None'}"
- Category: "${category || 'General'}"

Current items on this board:
${itemsContext || 'No items added yet.'}

Your task is to:
1. Provide an inspiring, encouraging analysis paragraph summarizing the core themes/moods observed on this board and explain how the recommended elements will help them expand their vision. (Make it max 3 sentences, warm and hyper-focused).
2. Recommend 3 to 4 hex color codes that extend or beautifully contrast with the board's vibe.
3. Generate exactly 3 fresh, highly tailored recommendations the user can add to their canvas:
   - 1 quote element (deeply aligned quote with author)
   - 1 note element (a 3-step actionable, specific checklist task list relevant to the current board's themes)
   - 1 image element (with a highly descriptive search query for Unsplash in "content" to represent the target vision, and an aesthetic photo motivation caption in "caption")

Return a JSON object matching this schema exactly:
{
  "analysis": "the analysis paragraph",
  "suggestedColorPalette": ["#hex1", "#hex2", "#hex3"],
  "recommendedItems": [
     {
        "type": "quote" | "note" | "text" | "image",
        "title": "Short title label of the recommendation",
        "content": "For quote type: write the quote only. For note type: 3 actionable bulleted steps. For image type: a highly descriptive keyword search concept to look up (e.g. 'cozy modern log cabin fireplace morning')",
        "caption": "For image type: a short tagline motivation caption (optional)",
        "color": "bg-indigo-50 border-indigo-200" | "bg-amber-50 border-amber-200" | "bg-rose-50 border-rose-200" | "bg-emerald-50 border-emerald-200" | "bg-cyan-50 border-cyan-200",
        "width": 25,
        "height": 28
     }
  ]
}
Ensure valid JSON output. Output nothing else.`;

    const response = await ai.models.generateContent({
      model: 'gemini-3.5-flash',
      contents: promptText,
      config: {
        responseMimeType: 'application/json',
        responseSchema: {
          type: Type.OBJECT,
          properties: {
            analysis: { type: Type.STRING },
            suggestedColorPalette: {
              type: Type.ARRAY,
              items: { type: Type.STRING }
            },
            recommendedItems: {
              type: Type.ARRAY,
              items: {
                type: Type.OBJECT,
                properties: {
                  type: { type: Type.STRING, description: "Must be 'quote', 'note', 'text', or 'image'" },
                  title: { type: Type.STRING },
                  content: { type: Type.STRING, description: "The content (quote payload, note steps checklist, or Unsplash search keyword query)" },
                  caption: { type: Type.STRING },
                  color: { type: Type.STRING },
                  width: { type: Type.INTEGER },
                  height: { type: Type.INTEGER }
                },
                required: ['type', 'title', 'content', 'width', 'height']
              }
            }
          },
          required: ['analysis', 'suggestedColorPalette', 'recommendedItems']
        }
      }
    });

    const resultText = response.text ? response.text.trim() : '';
    const outputData = JSON.parse(resultText);

    // Convert image keywords into featured unsplash queries
    outputData.recommendedItems = outputData.recommendedItems.map(item => {
      if (item.type === 'image' && !item.content.startsWith('http')) {
        const query = encodeURIComponent(item.content);
        item.content = `https://images.unsplash.com/featured/?${query}`;
      }
      return item;
    });

    res.json(outputData);
  } catch (error) {
    console.error('Gemini API Board Recommendations Error:', error);
    res.status(500).json({ error: 'Failed to synthesize board-specific AI suggestions. Confirm your Gemini API Key is configured.' });
  }
});

// Inspiration Gallery: powered by Server-Side Gemini AI API
app.post('/api/inspiration', authenticate, async (req, res) => {
  const { theme } = req.body;
  if (!theme) {
    return res.status(400).json({ error: 'Please choose an inspiration theme.' });
  }

  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey || apiKey === 'MY_GEMINI_API_KEY') {
    // Graceful fallback for missing key
    return res.json({
      theme,
      description: 'Your vision sparks here! Connect your Gemini API Key in Settings to get bespoke structured design grids and quotes.',
      quote: 'Action is the foundational key to all success. – Pablo Picasso',
      colorPalette: ['#f8fafc', '#ecfdf5', '#fffbeb', '#fff1f2'],
      suggestedItems: [
        {
          type: 'quote',
          title: 'Aspiration Statement',
          content: `Build an amazing path toward: "${theme}". Visualise every single milestone.`,
          color: 'bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800',
          width: 30,
          height: 25
        },
        {
          type: 'note',
          title: 'Key Intentions',
          content: '1. Commit to 15m focus\n2. Design the mood map\n3. Capture reference images',
          color: 'bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800',
          width: 25,
          height: 30
        },
        {
          type: 'image',
          title: 'Theme Focal Point',
          content: 'https://images.unsplash.com/photo-1498050108023-c5249f4df085?w=800',
          caption: `Inspiration search term for dream matching: ${theme}`,
          width: 40,
          height: 45
        }
      ]
    });
  }

  try {
    const ai = new GoogleGenAI({
      apiKey,
      httpOptions: {
        headers: {
          'User-Agent': 'aistudio-build',
        }
      }
    });

    const promptText = `Analyze this vision board theme: "${theme}". Generate a structured JSON response to help the user populate their digital vision board.
Create a response matching this schema:
{
  "theme": "the exact title provided",
  "description": "A encouraging, descriptive paragraph summarizing the emotional target and aesthetic visual directions of this theme (2-3 sentences max)",
  "quote": "A powerful quote (with author if possible) that fits the theme",
  "colorPalette": ["3 to 4 hexadecimal primary color hex codes fits the mood style"],
  "suggestedItems": [
     {
        "type": "quote" | "note" | "text" | "image",
        "title": "Short title label",
        "content": "For type 'quote': quote text. For 'note': bulleted markdown list of 3 actionable steps. For 'text': descriptive insight. For 'image': a highly descriptive keyword matching this aspect of the theme to lookup (e.g., 'cozy scandinavian cabin interior')",
        "caption": "For image type: brief 1-sentence photo motivation caption description (optional)",
        "color": "bg-indigo-50 border-indigo-200" | "bg-amber-50 border-amber-200" | "bg-rose-50 border-rose-200" | "bg-emerald-50 border-emerald-200" | "bg-cyan-50 border-cyan-200",
        "width": 30,
        "height": 25
     }
  ]
}
Include exactly 3 to 4 suggested items mapping quote, note, and image prompts. Ensure valid, beautifully structured JSON. Do not include markdown wraps or anything except the raw JSON.`;

    const response = await ai.models.generateContent({
      model: 'gemini-3.5-flash',
      contents: promptText,
      config: {
        responseMimeType: 'application/json',
        responseSchema: {
          type: Type.OBJECT,
          properties: {
            theme: { type: Type.STRING },
            description: { type: Type.STRING },
            quote: { type: Type.STRING },
            colorPalette: {
              type: Type.ARRAY,
              items: { type: Type.STRING }
            },
            suggestedItems: {
              type: Type.ARRAY,
              items: {
                type: Type.OBJECT,
                properties: {
                  type: { type: Type.STRING, description: "Must be 'image', 'text', 'quote', or 'note'" },
                  title: { type: Type.STRING },
                  content: { type: Type.STRING },
                  caption: { type: Type.STRING },
                  color: { type: Type.STRING },
                  width: { type: Type.INTEGER },
                  height: { type: Type.INTEGER }
                },
                required: ['type', 'title', 'content', 'width', 'height']
              }
            }
          },
          required: ['theme', 'description', 'quote', 'colorPalette', 'suggestedItems']
        }
      }
    });

    const resultText = response.text ? response.text.trim() : '';
    const outputData = JSON.parse(resultText);

    // Convert generic keywords generated for image-type source placeholder items into rich high-res Unsplash search queries
    outputData.suggestedItems = outputData.suggestedItems.map(item => {
      if (item.type === 'image' && !item.content.startsWith('http')) {
        const query = encodeURIComponent(item.content);
        item.content = `https://images.unsplash.com/photo-1519681393784-d120267933ba?w=800&q=80&sig=${Math.floor(Math.random() * 1000)}&auto=format&fit=crop`; // Beautiful default background or keyword injection representation
        // We can inject high quality search queries!
        item.content = `https://images.unsplash.com/featured/?${query}`;
      }
      return item;
    });

    res.json(outputData);
  } catch (error) {
    console.error('Gemini API Inspiration Error:', error);
    res.status(500).json({ error: 'Failed to generate model inspiration. Ensure your Gemini API Key is authorized.' });
  }
});

// Dynamic collaboration notifications endpoints
app.get('/api/collaborator-updates', authenticate, (req, res) => {
  const sampleActivities = [
    { collaborator: 'Alex Mercer', type: 'add', item: 'Sunset Vista Photography', board: 'Travel Wanderlust ✈️' },
    { collaborator: 'Sana Khan', type: 'move', item: 'Dream Loft layout card', board: 'Architectural Project' },
    { collaborator: 'Liam Davies', type: 'edit', item: '"Focus on process" quote', board: 'My Dream Journey 🪐' },
    { collaborator: 'Eva Patel', type: 'add', item: 'Action list note', board: 'Product Launch Goals' }
  ];

  const boards = readBoards();
  const validBoards = boards.filter(b => b.ownerId === req.user.id || b.collaborators.some(c => c.toLowerCase() === req.user.email.toLowerCase()));

  if (validBoards.length === 0) {
    return res.json([]);
  }

  // Create random notification centered on active user boards
  const randomActivity = sampleActivities[Math.floor(Math.random() * sampleActivities.length)];
  const targetBoard = validBoards[Math.floor(Math.random() * validBoards.length)];

  const update = {
    id: crypto.randomUUID(),
    boardId: targetBoard.id,
    boardTitle: targetBoard.title,
    collaborator: randomActivity.collaborator,
    activityType: randomActivity.type,
    itemName: randomActivity.item,
    timestamp: new Date().toISOString()
  };

  res.json([update]);
});

// Serve frontend client
async function startServer() {
  if (process.env.NODE_ENV !== 'production') {
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: 'spa',
    });
    app.use(vite.middlewares);
  } else {
    const distPath = path.join(process.cwd(), 'dist');
    app.use(express.static(distPath));
    app.get('*', (req, res) => {
      res.sendFile(path.join(distPath, 'index.html'));
    });
  }

  app.listen(PORT, '0.0.0.0', () => {
    console.log(`[Vision Board Server] listening dynamically on PORT ${PORT}`);
  });
}

startServer();
