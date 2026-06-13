-- =============================================
-- TRY Website Database Schema (Microsoft SQL Server)
-- =============================================

CREATE DATABASE TryWebsite;
GO

USE TryWebsite;
GO

-- a) activities
CREATE TABLE activities (
    id INT IDENTITY(1,1) PRIMARY KEY,
    title NVARCHAR(255) NOT NULL,
    description NVARCHAR(MAX) NULL,
    icon_name NVARCHAR(100) NULL,
    created_at DATETIME DEFAULT GETDATE()
);
GO

-- b) news
CREATE TABLE news (
    id INT IDENTITY(1,1) PRIMARY KEY,
    title NVARCHAR(255) NOT NULL,
    category NVARCHAR(100) NULL,
    content NVARCHAR(MAX) NULL,
    image_path NVARCHAR(255) NULL,
    published_at DATETIME NULL,
    is_published BIT DEFAULT 0
);
GO

-- c) impact_stories
CREATE TABLE impact_stories (
    id INT IDENTITY(1,1) PRIMARY KEY,
    quote_text NVARCHAR(MAX) NOT NULL,
    author_name NVARCHAR(100) NOT NULL,
    author_role NVARCHAR(100) NULL,
    created_at DATETIME DEFAULT GETDATE()
);
GO

-- d) gallery
CREATE TABLE gallery (
    id INT IDENTITY(1,1) PRIMARY KEY,
    image_path NVARCHAR(255) NOT NULL,
    caption NVARCHAR(255) NULL,
    created_at DATETIME DEFAULT GETDATE()
);
GO

-- e) messages
CREATE TABLE messages (
    id INT IDENTITY(1,1) PRIMARY KEY,
    name NVARCHAR(100) NOT NULL,
    email NVARCHAR(255) NOT NULL,
    subject NVARCHAR(255) NULL,
    message NVARCHAR(MAX) NOT NULL,
    received_at DATETIME DEFAULT GETDATE(),
    is_read BIT DEFAULT 0
);
GO

-- f) volunteers
CREATE TABLE volunteers (
    id INT IDENTITY(1,1) PRIMARY KEY,
    name NVARCHAR(100) NOT NULL,
    email NVARCHAR(255) NOT NULL,
    phone NVARCHAR(50) NULL,
    submitted_at DATETIME DEFAULT GETDATE(),
    status NVARCHAR(20) DEFAULT 'Pending' CHECK (status IN ('Pending', 'Approved', 'Rejected'))
);
GO

-- g) site_stats (single-row config table)
CREATE TABLE site_stats (
    id INT PRIMARY KEY,
    volunteers_count INT DEFAULT 0,
    projects_count INT DEFAULT 0,
    people_helped INT DEFAULT 0,
    years_active INT DEFAULT 0
);
GO
-- Initialize single row for stats
INSERT INTO site_stats (id, volunteers_count, projects_count, people_helped, years_active) VALUES (1, 0, 0, 0, 0);
GO

-- h) admin_users
CREATE TABLE admin_users (
    id INT IDENTITY(1,1) PRIMARY KEY,
    username NVARCHAR(100) UNIQUE NOT NULL,
    password_hash NVARCHAR(64) NOT NULL,  -- SHA-256 hash = 64 hex chars
    created_at DATETIME DEFAULT GETDATE()
);
GO
-- Insert default admin: username=admin, password=admin123
-- SHA-256 hash of 'admin123'
INSERT INTO admin_users (username, password_hash) VALUES ('admin', '240be518fabd2724ddb6f04eeb1da5967448d7e831c08c8fa822809f74c720a9');
GO
