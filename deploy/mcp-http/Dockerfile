FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY Directory.Packages.props .
COPY clio.slnx .
COPY clio/clio.csproj clio/

RUN dotnet restore clio/clio.csproj -p:TargetFrameworks=net10.0

COPY clio/ clio/

RUN dotnet publish clio/clio.csproj \
    -c Release \
    -o /app/published \
    --framework net10.0 \
    --no-restore \
    /p:AssemblyName=clio-app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app
COPY --from=build /app/published .

USER $APP_UID

EXPOSE 8005

ENTRYPOINT ["dotnet", "/app/clio-app.dll", "mcp-http", "--host", "0.0.0.0", "--port", "8005"]
