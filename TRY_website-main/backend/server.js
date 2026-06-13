const express = require('express');
const cors = require('cors');
const sql = require('mssql/msnodesqlv8');

const app = express();
const PORT = 5138;

app.use(cors({
  origin: ['http://localhost:5501', 'https://localhost:5501']
}));
app.use(express.json());

const dbConfig = {
  connectionString: 'Driver={ODBC Driver 18 for SQL Server};Server=localhost;Database=TryWebsite;Trusted_Connection=yes;TrustServerCertificate=yes;'
};

// Test database connection on startup
async function connectDb() {
  try {
    await sql.connect(dbConfig);
    console.log('✅ Connected to SQL Server database "TryWebsite" successfully!');
  } catch (err) {
    console.error('❌ Database connection failed:', err.message);
  }
}
connectDb();

app.get('/api/health', (req, res) => {
  res.json({ status: 'ok' });
});

app.get('/api/site-info', (req, res) => {
  res.json({
    name: "TRY - KUET Social Service Club",
    description: "Node.js backend for the TRY website",
    frontendOrigin: "http://localhost:5501"
  });
});

app.get('/api/site-stats', async (req, res) => {
  try {
    const result = await sql.query`SELECT volunteers_count, projects_count, people_helped, years_active FROM site_stats WHERE id = 1`;
    if (result.recordset.length > 0) {
      const row = result.recordset[0];
      res.json({
        volunteers: row.volunteers_count,
        projects: row.projects_count,
        peopleHelped: row.people_helped,
        yearsActive: row.years_active
      });
    } else {
      res.status(404).json({ error: 'Stats not found' });
    }
  } catch (err) {
    console.error('Error fetching site stats:', err.message);
    res.status(500).json({ error: 'Database error' });
  }
});

app.post('/api/messages', async (req, res) => {
  const { name, email, subject, message } = req.body;
  if (!name || !email || !message) {
    return res.status(400).json({ success: false, error: 'Name, email, and message are required' });
  }

  try {
    const request = new sql.Request();
    request.input('name', sql.NVarChar(100), name);
    request.input('email', sql.NVarChar(255), email);
    request.input('subject', sql.NVarChar(255), subject || null);
    request.input('message', sql.NVarChar(sql.MAX), message);

    await request.query(`
      INSERT INTO messages (name, email, subject, message, received_at, is_read)
      VALUES (@name, @email, @subject, @message, GETDATE(), 0)
    `);

    res.json({ success: true });
  } catch (err) {
    console.error('Error saving message:', err.message);
    res.status(500).json({ success: false, error: 'Database error' });
  }
});

app.listen(PORT, () => {
  console.log(`🚀 TRY Backend API Server running at http://localhost:${PORT}`);
  console.log(`   Health check:  http://localhost:${PORT}/api/health`);
  console.log(`   Site stats:    http://localhost:${PORT}/api/site-stats`);
  console.log(`   Messages:      POST http://localhost:${PORT}/api/messages`);
});
