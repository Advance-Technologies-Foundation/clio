FROM mcr.microsoft.com/dotnet/runtime:6.0 as runtime
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY clio clio
COPY clio.sln clio.sln
COPY README.md README.md

WORKDIR /app/clio
RUN dotnet publish -c Release -o /app/published --framework net6.0

FROM runtime AS final
WORKDIR /app
COPY --from=build /app/published .
LABEL service=clio

ENTRYPOINT ["dotnet", "/app/clio.dll"]
