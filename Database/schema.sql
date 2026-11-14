CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(50) UNIQUE NOT NULL,
    quota_bytes BIGINT NOT NULL DEFAULT 10737418240,
    used_bytes BIGINT NOT NULL DEFAULT 0 CHECK (used_bytes >= 0),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT used_bytes_within_quota CHECK (used_bytes <= quota_bytes)
);

CREATE TABLE folders (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    parent_id UUID REFERENCES folders(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX folders_unique_name_per_parent
ON folders (owner_id, COALESCE(parent_id, '00000000-0000-0000-0000-000000000000'::uuid), name);

CREATE FUNCTION folder_name_not_taken_by_file() RETURNS trigger AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM files
        WHERE owner_id = NEW.owner_id
          AND folder_id IS NOT DISTINCT FROM NEW.parent_id
          AND name = NEW.name
    ) THEN
        RAISE EXCEPTION 'File named ''%'' already exists in this location', NEW.name
            USING ERRCODE = 'unique_violation';
END IF;

RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER folders_name_check
    BEFORE INSERT OR UPDATE OF name, parent_id ON folders
    FOR EACH ROW EXECUTE FUNCTION folder_name_not_taken_by_file();

CREATE TABLE files (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    folder_id UUID REFERENCES folders(id) ON DELETE CASCADE,
    name VARCHAR(255) NOT NULL,
    size_bytes BIGINT NOT NULL,
    mime_type VARCHAR(127),
    storage_path TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX files_unique_name_per_folder
ON files (owner_id, COALESCE(folder_id, '00000000-0000-0000-0000-000000000000'::uuid), name);

CREATE FUNCTION file_name_not_taken_by_folder() RETURNS trigger AS $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM folders
        WHERE owner_id = NEW.owner_id
          AND parent_id IS NOT DISTINCT FROM NEW.folder_id
          AND name = NEW.name
    ) THEN
        RAISE EXCEPTION 'Folder named ''%'' already exists in this location', NEW.name
            USING ERRCODE = 'unique_violation';
END IF;

RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER files_name_check
    BEFORE INSERT OR UPDATE OF name, folder_id ON files
    FOR EACH ROW EXECUTE FUNCTION file_name_not_taken_by_folder();

CREATE TABLE activity (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    owner_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    action VARCHAR(32) NOT NULL,
    target_type VARCHAR(16) NOT NULL,
    target_id UUID NOT NULL,
    target_name VARCHAR(255) NOT NULL,
    folder_id UUID,
    bytes BIGINT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX activity_owner_created ON activity (owner_id, created_at DESC);

INSERT INTO users (username, quota_bytes) VALUES ('test-user', 10737418240) RETURNING id;
