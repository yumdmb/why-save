CREATE VIRTUAL TABLE files_fts USING fts5(
    filename,
    project,
    url,
    content='files',
    content_rowid='rowid'
);

CREATE TRIGGER files_ai AFTER INSERT ON files BEGIN
    INSERT INTO files_fts(rowid, filename, project, url)
    VALUES (new.rowid, new.filename, new.project, new.url);
END;

CREATE TRIGGER files_ad AFTER DELETE ON files BEGIN
    INSERT INTO files_fts(files_fts, rowid, filename, project, url)
    VALUES ('delete', old.rowid, old.filename, old.project, old.url);
END;

CREATE TRIGGER files_au AFTER UPDATE ON files BEGIN
    INSERT INTO files_fts(files_fts, rowid, filename, project, url)
    VALUES ('delete', old.rowid, old.filename, old.project, old.url);
    INSERT INTO files_fts(rowid, filename, project, url)
    VALUES (new.rowid, new.filename, new.project, new.url);
END;
