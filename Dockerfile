FROM amd64/ubuntu:22.04
RUN apt-get -y update && apt-get -y install libicu-dev
ENV ASPNETCORE_URLS="http://localhost:5008"
COPY migrations-bin/ .
COPY dotnet-bin-folder/ .
COPY entrypoint.sh .
RUN pwd
RUN ls -al .
RUN chmod +x /migrationbundle
RUN chmod +x /MatGPT
RUN chmod +x /entrypoint.sh
RUN ls -al .
ENTRYPOINT ["./entrypoint.sh"]
