FROM mcr.microsoft.com/dotnet/sdk:10.0 AS sdk
WORKDIR /src
COPY KargoNet.sln ./
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet restore src/Services/Shipment.Api
RUN dotnet publish src/Services/Shipment.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=sdk /app/publish .
USER app
EXPOSE 5001
ENTRYPOINT ["dotnet", "/app/Shipment.Api.dll"]
