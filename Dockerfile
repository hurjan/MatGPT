FROM amd64/ubuntu:22.04
RUN apt-get -y update && apt-get -y install libicu-dev
ENV ASPNETCORE_URLS="http://localhost:5008"
COPY selfc-linux-bundle .
COPY publish .
COPY entrypoint.sh .
RUN chmod +x selfc-linux-bundle
RUN chmod +x MatGPT_deploy
RUN chmod +x entrypoint.sh
RUN ls -al .
ENTRYPOINT ["./entrypoint.sh"]
