FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set the working directory to /app
WORKDIR /app

# Copy the project files to the container
COPY clio clio
COPY clio.sln clio.sln
COPY README.md README.md

# Change the working directory to /app/clio
WORKDIR /app/clio

# Publish the project, changing the output DLL name to avoid conflicts
RUN dotnet publish -c Release -o /app/published --framework net8.0 /p:AssemblyName=clio-app

# Final stage: Use the build stage as the base for the final image
FROM build AS final

# Set the working directory to /app
WORKDIR /app

# Remove the clio directory if it exists, to avoid any conflict with clio.dll
RUN rm -rf /app/clio

# Copy the published files from the build stage to the final image
COPY --from=build /app/published /app/

# Add a label to the image
LABEL service=clio

# Set the entry point to run the clio-app.dll
ENTRYPOINT ["dotnet", "/app/clio-app.dll"]