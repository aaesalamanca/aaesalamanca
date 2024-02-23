FROM httpd:alpine
COPY ./docs/ /usr/local/apache2/htdocs
