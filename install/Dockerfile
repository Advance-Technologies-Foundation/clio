FROM mcr.microsoft.com/dotnet/sdk:3.1 as base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build
WORKDIR /app

COPY clio clio
COPY clio.sln clio.sln

WORKDIR /app/clio
RUN dotnet publish -c Release -o /app/published

FROM base AS final
WORKDIR /app
COPY --from=build /app/published .
LABEL service=clio

ENTRYPOINT ["dotnet", "/app/clio.dll"]
