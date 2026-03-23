#!/bin/bash
set -e

DB_HOST=${DB_HOST:-host.docker.internal}
DB_PORT=${DB_PORT:-5432}
DB_NAME=${DB_NAME:-creatio}
DB_USER=${DB_USER:-postgres}
DB_PASSWORD=${DB_PASSWORD:-root}
REDIS_HOST=${REDIS_HOST:-$DB_HOST}
REDIS_PORT=${REDIS_PORT:-6379}
REDIS_DB=${REDIS_DB:-0}

if [ -f /app/ConnectionStrings.config.template ]; then
    envsubst < /app/ConnectionStrings.config.template > /app/ConnectionStrings.config
fi

mkdir -p /run/sshd

exec /usr/bin/supervisord -c /etc/supervisor/conf.d/supervisord.conf
