ARG BASE_IMAGE=creatio-base:8.0-v1
FROM ${BASE_IMAGE}

ENV ASPNETCORE_ENVIRONMENT=Development \
    PATH="$PATH:/root/.dotnet/tools"

LABEL org.creatio.capability.ssh="true" \
      org.creatio.capability.code-server="true"

RUN apt-get update && \
    apt-get -y --no-install-recommends install \
    openssh-server \
    gettext-base && \
    apt-get clean all && \
    rm -rf /var/lib/apt/lists/* /var/cache/apt/* && \
    mkdir -p /var/run/sshd && \
    echo 'root:Supervisor' | chpasswd && \
    sed -i 's/#PermitRootLogin prohibit-password/PermitRootLogin yes/' /etc/ssh/sshd_config && \
    sed -i 's/#PasswordAuthentication yes/PasswordAuthentication yes/' /etc/ssh/sshd_config && \
    mkdir -p /run/sshd

COPY code-server.tar.gz /tmp/code-server.tar.gz
RUN tar -C /usr/local/lib -xzf /tmp/code-server.tar.gz && \
    ln -sfn "$(find /usr/local/lib -maxdepth 1 -type d -name 'code-server-*-linux-amd64' | sort | tail -n 1)/bin/code-server" /usr/local/bin/code-server && \
    rm -f /tmp/code-server.tar.gz

RUN dotnet tool install -g clio

RUN printf '%s\n' \
    'case ":$PATH:" in' \
    '  *:/root/.dotnet/tools:*) ;;' \
    '  *) export PATH="$PATH:/root/.dotnet/tools" ;;' \
    'esac' > /etc/profile.d/clio-tools.sh && \
    chmod 0644 /etc/profile.d/clio-tools.sh && \
    printf '\n%s\n' \
    'case ":$PATH:" in' \
    '  *:/root/.dotnet/tools:*) ;;' \
    '  *) export PATH="$PATH:/root/.dotnet/tools" ;;' \
    'esac' >> /root/.bashrc

RUN mkdir -p /root/creatio/clio && \
    cat <<'EOF' > /root/creatio/clio/appsettings.json
{
  "$schema": "./schema.json",
  "ActiveEnvironmentKey": "dev",
  "Autoupdate": false,
  "Environments": {
    "dev": {
      "Uri": "http://localhost:5000",
      "Login": "Supervisor",
      "Password": "Supervisor",
      "IsNetCore": true,
      "EnvironmentPath": "/app"
    }
  }
}
EOF

WORKDIR /app
COPY source/ ./
RUN sed -i 's|<add key="CookiesSameSiteMode" value="[^"]*" */>|<add key="CookiesSameSiteMode" value="Lax" />|g' /app/Terrasoft.WebHost.dll.config && \
    sed -i 's/"CustomizeDataProtection": false/"CustomizeDataProtection": true/g' /app/appsettings.json && \
    mkdir -p /var/log/supervisor

COPY supervisord.conf /etc/supervisor/conf.d/supervisord.conf
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 5000 5002 22 8080

ENTRYPOINT ["/entrypoint.sh"]
