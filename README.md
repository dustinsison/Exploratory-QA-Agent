# Exploratory QA Agent

Give the agent a URL and a plain-English testing charter. A local Ollama model chooses actions from the browser’s accessibility snapshot. Playwright executes them and saves a Markdown report with the recorded steps, a final screenshot, and a link to the browser recording. The runner needs no API key.

## Run the storefront demo

Install Node.js 22 or newer and [Ollama](https://ollama.com/download). Open the Ollama app or run `ollama serve` if the service isn’t already running. From the repository folder, run these commands in PowerShell or a terminal.

```sh
ollama pull qwen2.5:7b
npm ci
npx playwright install chromium
npm run demo
```

Leave the storefront running. In a second terminal in the same folder, start the QA run.

```sh
npm run qa -- --url http://127.0.0.1:3000 --charter "Search for hiking boots. Select size 12. Verify the empty state, then clear filters and check that matching products return."
```

The storefront contains a seeded defect. **Clear filters** leaves size 12 selected, so the two matching boots stay hidden. Open the report under `artifacts/` to review the steps and screenshot or play the WebM recording. Each WebM is saved under `artifacts/run-*/video/` when the browser closes. Keep the report and its video folder together to preserve the recording link. The video includes time spent waiting for Ollama.

The runner uses Ollama at `127.0.0.1:11434`. To try a smaller model on your PC, run `ollama pull qwen2.5:3b` and add `--model qwen2.5:3b` to the QA command. Model choice can affect which checks the agent performs and which findings it reports.

## Scope

Use a local or authorized test site and synthetic inputs. The runner allows requests to the starting origin, which can limit sites that use external assets or identity providers. Page snapshots go to the local Ollama service, and reports, screenshots, and recordings may contain test inputs or page content. The runner covers functional UI checks such as search and filters. Sign-in testing with managed test credentials is a future milestone.

Run `npm run check` to type-check the project.
