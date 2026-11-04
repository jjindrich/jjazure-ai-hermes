FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS dotnet

FROM nousresearch/hermes-agent@sha256:a86ace4d4177485ce136c1160ad65a641211268e733e7ea6078fe6761bacb06e
ENV DOTNET_ROOT=/usr/share/dotnet \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
COPY .build/publish /app

RUN chown hermes:hermes /opt/data && chmod 700 /opt/data
USER hermes
EXPOSE 8080
ENTRYPOINT ["/usr/share/dotnet/dotnet", "/app/Trip.dll"]
