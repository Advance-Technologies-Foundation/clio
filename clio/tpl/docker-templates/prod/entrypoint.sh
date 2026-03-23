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

escape_sed_replacement() {
    printf '%s' "$1" | sed -e 's/[\\/&|]/\\&/g'
}

if [ -f /app/ConnectionStrings.config.template ]; then
    db_host_escaped=$(escape_sed_replacement "$DB_HOST")
    db_port_escaped=$(escape_sed_replacement "$DB_PORT")
    db_name_escaped=$(escape_sed_replacement "$DB_NAME")
    db_user_escaped=$(escape_sed_replacement "$DB_USER")
    db_password_escaped=$(escape_sed_replacement "$DB_PASSWORD")
    redis_host_escaped=$(escape_sed_replacement "$REDIS_HOST")
    redis_port_escaped=$(escape_sed_replacement "$REDIS_PORT")
    redis_db_escaped=$(escape_sed_replacement "$REDIS_DB")

    sed \
        -e "s|\${DB_HOST}|$db_host_escaped|g" \
        -e "s|\${DB_PORT}|$db_port_escaped|g" \
        -e "s|\${DB_NAME}|$db_name_escaped|g" \
        -e "s|\${DB_USER}|$db_user_escaped|g" \
        -e "s|\${DB_PASSWORD}|$db_password_escaped|g" \
        -e "s|\${REDIS_HOST}|$redis_host_escaped|g" \
        -e "s|\${REDIS_PORT}|$redis_port_escaped|g" \
        -e "s|\${REDIS_DB}|$redis_db_escaped|g" \
        /app/ConnectionStrings.config.template > /app/ConnectionStrings.config
fi

mkdir -p /var/run /var/log/supervisor

exec /usr/bin/supervisord -c /etc/supervisor/conf.d/supervisord.conf
