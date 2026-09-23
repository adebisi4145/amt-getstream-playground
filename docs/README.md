# docs

Project documentation, such as architecture notes, decisions and setup guides.

| Document | What it covers |
| --- | --- |
| [api.md](api.md) | How the web app gets a Stream Video token from the API, including a `tokenProvider` example, the rules the web app can rely on, and notes on how the Stream SDK behaves |
| [api-node.md](api-node.md) | The Node.js API: setup, the call, user and webhook endpoints, how the web app makes voice and video calls with the Stream SDK, and notes on how the Node SDK behaves |

Request and response shapes aren't repeated here. They come from the API's OpenAPI document, which you can browse at http://localhost:5056/scalar while the API runs in Development.
