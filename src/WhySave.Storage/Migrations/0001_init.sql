PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE files (
    id                  TEXT     PRIMARY KEY NOT NULL,
    path                TEXT     NOT NULL,
    filename            TEXT     NOT NULL,
    ext                 TEXT,
    size_bytes          INTEGER  NOT NULL,
    volume_serial       INTEGER,
    ntfs_file_id        INTEGER,
    sha256              TEXT,
    status              TEXT     NOT NULL CHECK (status IN ('pending','contexted','legacy','missing')),
    reason_cipher       BLOB,
    notes_cipher        BLOB,
    project             TEXT,
    url                 TEXT,
    referrer            TEXT,
    tab_title           TEXT,
    parent_file_id      TEXT     REFERENCES files(id),
    first_seen_at       INTEGER  NOT NULL,
    saved_at            INTEGER,
    last_prompted_at    INTEGER,
    last_resolved_at    INTEGER,
    last_opened_via_app_at INTEGER,
    created_at          INTEGER  NOT NULL,
    updated_at          INTEGER  NOT NULL
);

CREATE INDEX idx_files_path         ON files(path);
CREATE INDEX idx_files_status       ON files(status);
CREATE INDEX idx_files_sha256       ON files(sha256);
CREATE INDEX idx_files_volume_ntfs  ON files(volume_serial, ntfs_file_id);
