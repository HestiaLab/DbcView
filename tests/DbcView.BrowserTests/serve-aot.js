const http = require("http");
const fs = require("fs");
const path = require("path");

const root = path.resolve(process.argv[2] || "wwwroot");
const port = Number(process.argv[3] || 5210);

const contentTypes = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".wasm": "application/wasm",
    ".dat": "application/octet-stream",
    ".blazor": "application/octet-stream",
    ".dll": "application/octet-stream",
    ".pdb": "application/octet-stream",
    ".svg": "image/svg+xml",
    ".woff": "font/woff",
    ".woff2": "font/woff2",
    ".ico": "image/x-icon",
    ".txt": "text/plain; charset=utf-8",
};

const server = http.createServer((request, response) => {
    const requestPath = decodeURIComponent((request.url || "/").split("?")[0]);
    const relative = requestPath.endsWith("/") ? requestPath + "index.html" : requestPath;
    const filePath = path.join(root, relative);

    if (!filePath.startsWith(root)) {
        response.writeHead(403);
        response.end("forbidden");
        return;
    }

    fs.readFile(filePath, (error, data) => {
        if (error) {
            fs.readFile(path.join(root, "index.html"), (fallbackError, index) => {
                if (fallbackError) {
                    response.writeHead(404);
                    response.end("not found");
                    return;
                }

                response.writeHead(200, { "Content-Type": contentTypes[".html"] });
                response.end(index);
            });
            return;
        }

        const extension = path.extname(filePath).toLowerCase();
        response.writeHead(200, {
            "Content-Type": contentTypes[extension] || "application/octet-stream",
        });
        response.end(data);
    });
});

server.listen(port, "127.0.0.1", () => {
    console.log(`serving ${root} at http://127.0.0.1:${port}`);
});
