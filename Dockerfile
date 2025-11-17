# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/InvictiScanner/InvictiScanner.csproj src/InvictiScanner/
RUN dotnet restore src/InvictiScanner/InvictiScanner.csproj

COPY . .
RUN dotnet publish src/InvictiScanner/InvictiScanner.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    SCB_SCAN_DURATION=short \
    SCB_SCANNER__OUTPUTPATH=/home/scanner/results

COPY --from=build /app/publish .
COPY invicti.json ./swagger/invicti.json

RUN useradd --create-home --shell /bin/bash scb \
    && mkdir -p /home/scanner/results \
    && chown -R scb:scb /home/scanner /app

USER scb
VOLUME ["/home/scanner/results"]

ENTRYPOINT ["dotnet", "InvictiScanner.dll"]
