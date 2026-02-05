-- Perf fixture: creates a wide-ish table and inserts 100,000 rows using a recursive CTE.
-- Designed to avoid reliance on generate_series.

DROP TABLE IF EXISTS perf_items;

CREATE TABLE perf_items (
    id              BIGINT PRIMARY KEY,
    created_at      TIMESTAMP NOT NULL,
    category        TEXT NOT NULL,
    score           INTEGER NOT NULL,
    payload         TEXT NOT NULL
);

WITH RECURSIVE seq(n) AS (
    SELECT 1
    UNION ALL
    SELECT n + 1 FROM seq WHERE n < 100000
)
INSERT INTO perf_items (id, created_at, category, score, payload)
SELECT
    n::bigint AS id,
    (TIMESTAMP '2020-01-01' + (n || ' seconds')::interval) AS created_at,
    CASE (n % 10)
        WHEN 0 THEN 'alpha'
        WHEN 1 THEN 'bravo'
        WHEN 2 THEN 'charlie'
        WHEN 3 THEN 'delta'
        WHEN 4 THEN 'echo'
        WHEN 5 THEN 'foxtrot'
        WHEN 6 THEN 'golf'
        WHEN 7 THEN 'hotel'
        WHEN 8 THEN 'india'
        ELSE 'juliet'
    END AS category,
    (n % 1000)::int AS score,
    ('payload-' || n || '-' || repeat('x', 64)) AS payload
FROM seq;

-- Helpful indexes for demo queries
CREATE INDEX IF NOT EXISTS ix_perf_items_category ON perf_items(category);
CREATE INDEX IF NOT EXISTS ix_perf_items_score ON perf_items(score);
CREATE INDEX IF NOT EXISTS ix_perf_items_created_at ON perf_items(created_at);
