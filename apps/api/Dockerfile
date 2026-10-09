# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# restore trước (tận dụng cache layer)
COPY Directory.Build.props Directory.Packages.props ./
COPY src/HocLieu.Api/HocLieu.Api.csproj src/HocLieu.Api/
COPY src/HocLieu.QuizImport/HocLieu.QuizImport.csproj src/HocLieu.QuizImport/
RUN dotnet restore src/HocLieu.Api/HocLieu.Api.csproj

COPY src/HocLieu.Api src/HocLieu.Api
COPY src/HocLieu.QuizImport src/HocLieu.QuizImport
RUN dotnet publish src/HocLieu.Api/HocLieu.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV DOTNET_GCServer=0
COPY --from=build /app/publish .
EXPOSE 8080
# Lắng nghe $PORT (Render) hoặc 8080 nếu $PORT chưa đặt (docker compose).
ENTRYPOINT ["sh", "-c", "dotnet HocLieu.Api.dll --urls http://+:${PORT:-8080}"]
