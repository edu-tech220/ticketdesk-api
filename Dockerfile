FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/TicketDesk.Api/TicketDesk.Api.csproj src/TicketDesk.Api/
RUN dotnet restore src/TicketDesk.Api/TicketDesk.Api.csproj

COPY src/TicketDesk.Api/ src/TicketDesk.Api/
RUN dotnet publish src/TicketDesk.Api/TicketDesk.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN addgroup --gid 1000 appgroup && adduser --uid 1000 --ingroup appgroup --disabled-password appuser
COPY --from=build /app/publish .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "TicketDesk.Api.dll"]
