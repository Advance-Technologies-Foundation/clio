FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Set the working directory to /app
WORKDIR /app

# Copy the project files to the container
COPY clio clio
COPY clio.sln clio.sln
COPY README.md README.md
# Change the working directory to /app/clio
WORKDIR /app/clio

RUN dotnet publish ./clio.csproj -c Release -o /app/published --framework net10.0 /p:AssemblyName=clio-app
# Final stage: Use the build stage as the base for the final image
FROM build AS final

# Set the working directory to /app
WORKDIR /app

# Remove the clio directory if it exists, to avoid any conflict with clio.dll
RUN rm -rf /app/clio

# Copy the published files from the build stage to the final image
COPY --from=build /app/published /app/

# Create a dedicated non-root user to run the app as (GH #1161: image ran as root).
# The group is created explicitly with a pinned gid instead of relying on useradd's
# default same-named-group behavior (Debian's USERGROUPS_ENAB=yes), so the build does
# not depend on that default staying enabled in a future base-image swap.
# --no-log-init avoids a known useradd issue with sparse/large log files for high
# UIDs on some Debian-based images (this image is Debian-based).
# --no-create-home is fine because ENV HOME=/app below points HOME at a directory
# we already chown to this user.
RUN groupadd -g 10001 clioapp \
    && useradd --uid 10001 --gid 10001 --no-log-init --no-create-home --shell /usr/sbin/nologin clioapp \
    && chown -R clioapp:clioapp /app

# clio resolves its settings/config home via Environment.SpecialFolder.UserProfile,
# which reads $HOME on Linux. Without this, a --no-create-home non-root user has no
# $HOME and any clio command that writes settings would fail.
ENV HOME=/app

USER clioapp

# Add a label to the image
LABEL service=clio

# Set the entry point to run the clio-app.dll
ENTRYPOINT ["dotnet", "/app/clio-app.dll"]