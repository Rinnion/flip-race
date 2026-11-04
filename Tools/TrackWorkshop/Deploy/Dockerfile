FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY app/ ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrackWorkshop.dll"]
