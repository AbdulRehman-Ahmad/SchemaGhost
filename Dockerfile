FROM node:24-alpine AS frontend
WORKDIR /web

COPY web/package*.json ./
RUN npm ci

COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src

COPY src/SchemaGhost/ src/SchemaGhost/
COPY src/SchemaGhost.Api/ src/SchemaGhost.Api/

RUN dotnet publish src/SchemaGhost.Api/SchemaGhost.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=backend /app/publish ./
COPY --from=frontend /web/dist/schemaghost-web/browser/ ./wwwroot/

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SchemaGhost.Api.dll"]