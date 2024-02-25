# $ docker build -t aaesalamanca:1.0.0 .
# $ docker run --name aaesalamanca -d -p 8080:80 aaesalamanca:1.0.0
FROM nginx:alpine
COPY ./docs/ /usr/share/nginx/html
