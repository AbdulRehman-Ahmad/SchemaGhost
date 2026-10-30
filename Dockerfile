FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/SchemaGhost/ src/SchemaGhost/
COPY src/SchemaGhost.Api/ src/SchemaGhost.Api/
RUN dotnet publish src/SchemaGhost.Api/SchemaGhost.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "SchemaGhost.Api.dll"]
