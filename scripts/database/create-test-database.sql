-- Create test database for SportRental
-- Run this on Azure PostgreSQL to create sr_test database

-- Connect to default postgres database first, then run:
CREATE DATABASE sr_test
    WITH 
    OWNER = synapsis
    ENCODING = 'UTF8'
    CONNECTION LIMIT = -1;

COMMENT ON DATABASE sr_test IS 'SportRental test database for integration tests';

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE sr_test TO synapsis;
