FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
WORKDIR /src
COPY KargoNet.sln ./
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/Services/Tracking.Api
RUN dotnet publish src/Services/Tracking.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=sdk /app/publish .
USER app
EXPOSE 5002
ENTRYPOINT ["/app/Tracking.Api.dll"]
