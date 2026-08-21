FROM busybox:1.36.1

LABEL org.creatio.capability.db="true"

WORKDIR /db
COPY db/ ./

# GH #1161: this is a data-only image (no ENTRYPOINT/running process, used only as a
# labeled source to copy files from), so it can run as a non-root, unprivileged UID.
# A numeric USER works without a /etc/passwd entry, which keeps this independent of
# busybox's applet set (e.g. adduser).
RUN chown -R 10001:10001 /db
USER 10001:10001
