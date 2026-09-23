ARG BASE_IMAGE=creatio-base:8.0-v1
FROM ${BASE_IMAGE}

LABEL org.creatio.capability.ssh="false" \
      org.creatio.capability.code-server="false"
      
COPY entrypoint.sh /entrypoint.sh
COPY supervisord.conf /etc/supervisor/conf.d/supervisord.conf
RUN chmod +x /entrypoint.sh

WORKDIR /app
COPY source/ ./
RUN sed -i 's|<add key="CookiesSameSiteMode" value="[^"]*" */>|<add key="CookiesSameSiteMode" value="Lax" />|g' /app/Terrasoft.WebHost.dll.config && \
    sed -i 's/"CustomizeDataProtection": false/"CustomizeDataProtection": true/g' /app/appsettings.json

# OpenShift / arbitrary non-root UID hardening (mirrors core PR #1076).
# The k8s chart (tools/creatio-helm) overrides ENTRYPOINT, so entrypoint.sh
# and supervisord never run in-cluster; their root assumptions are out of
# scope here and affect standalone `docker run` (root by default) only.
# No USER directive: OpenShift assigns a UID from the project range (gid 0).
#
# Scope of the group-0 grant, stated precisely so it is not over-read:
#   * `g=rX` tree-wide gives group READ (and execute on directories) everywhere.
#   * Group WRITE is re-granted only on /app itself, the two subtrees the chart
#     writes, and the two config files it overwrites.
#   * /app itself MUST be group-writable: the chart creates NEW files there (its
#     start-up fail-fast writes /app/.probe, and ConnectionStrings.config is
#     copied in). A writable directory implies group 0 can delete and recreate
#     any top-level entry, including assemblies. So `g=rX` on the assemblies
#     prevents IN-PLACE modification, not replacement. That is inherent to the
#     chart's write requirement, not an oversight — do not read it as immutability.
#
# The /conf -> /app/conf and /Terrasoft.Configuration -> /app/Terrasoft.Configuration
# compatibility links are created through `ensure_link <target> <link>`, which is
# idempotent for exactly one pre-existing state and strict for every other one:
#   * link path absent -> created with `ln -sT`.
#   * link path is a symlink whose `readlink` is exactly <target> -> kept as is. This
#     is the expected state when BASE_IMAGE is already hardened: the Creatio .NET 10
#     product image (core Terrasoft.WebHost/Dockerfile, ENG-95158) and every image a
#     previous `--template prod` build produced carry both links, and platform-update
#     builds layer onto them through `--base-image` (ENG-94408).
#   * anything else (a real file or directory, or a symlink to any other target,
#     dangling or not) -> the build fails with the offending `ls -ld` line.
# `ln` never gets -f, so an unexpected layout is never silently replaced and /conf can
# never end up pointing anywhere but the group-writable /app/conf the chart writes.
# RUN uses the default /bin/sh (no SHELL directive), so the guard is POSIX sh only.
RUN mkdir -p /app/conf /app/Terrasoft.Configuration && \
    chmod -R g=rX /app && \
    chmod g=u /app && \
    chmod -R g=u /app/conf /app/Terrasoft.Configuration && \
    find /app -maxdepth 1 \( -name ConnectionStrings.config -o -name Terrasoft.WebHost.dll.config \) -exec chmod g=u {} + && \
    find /app -type f -name '*.pfx' -exec chmod 0640 {} + && \
    ensure_link() { \
        if [ -L "$2" ] && [ "$(readlink "$2")" = "$1" ]; then \
            echo "$2 already links to $1 in the base image; keeping it"; \
        elif [ -e "$2" ] || [ -L "$2" ]; then \
            echo "ERROR: $2 already exists and is not a symlink to $1: $(ls -ld "$2"). Refusing to replace it; use a base image where $2 is absent or already links to $1." >&2; \
            return 1; \
        else \
            ln -sT "$1" "$2"; \
        fi; \
    } && \
    ensure_link /app/conf /conf && \
    ensure_link /app/Terrasoft.Configuration /Terrasoft.Configuration && \
    test -e /conf && test -e /Terrasoft.Configuration

EXPOSE 5000 5002

ENTRYPOINT ["/entrypoint.sh"]
