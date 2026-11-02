-- Runs once, when the volume is created. Each service owns its database; no service reads another's.
CREATE DATABASE identity;
CREATE DATABASE catalog;
CREATE DATABASE basket;
CREATE DATABASE inventory;
CREATE DATABASE ordering;
CREATE DATABASE payment;
