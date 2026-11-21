# Exploratory QA Agent

Give the agent a URL and a plain-English testing charter. The runner builds a list of available actions from the browser’s accessibility snapshot and native dropdowns. A local Ollama model uses the charter, page state, and test history to choose the next action. The list refreshes after each decision. Playwright executes them and saves a Markdown report with the recorded steps, a final screenshot, and a link to the browser recording. The runner needs no API key.

## Windows desktop app

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), Node.js 22 or newer, and Ollama. In PowerShell, from the repository root:

```powershell
npm ci
npx playwright install chromium
dotnet run --project desktop
```

Start Ollama before opening the app. The model picker lists installed models. Use **Refresh models** after downloading a model. Enter a URL and charter, choose the model, and click **Run test**. For dustinsison.com, enter `https://c0.wp.com` under **Allowed asset origins** and enable **Allow YouTube embeds** if the charter needs the background player. Enter multiple asset origins separated by commas.

Click **Open artifacts** after the run to see its report, screenshot, and video. Both the app and the command line use folders such as `artifacts/run-dustinsison.com-2026-11-21-17-30-00-125/`. The name uses the starting hostname and local date and time, including milliseconds.

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

For a repeatable storefront run, use `npm run qa -- --storefront` in the second terminal. This runs the fixed search and filter scenario with Playwright, checks the results, and saves the same evidence without starting Ollama.

The storefront contains a seeded defect. **Clear filters** leaves size 12 selected, so the two matching boots stay hidden. Open the report under `artifacts/` to review the steps and screenshot or play the WebM recording. Each WebM is saved under `artifacts/run-*/video/` when the browser closes. Keep the report and its video folder together to preserve the recording link. The video includes time spent waiting for Ollama.

The runner uses Ollama at `127.0.0.1:11434`. To try a smaller model on your PC, run `ollama pull qwen2.5:3b` and add `--model qwen2.5:3b` to the QA command. Model choice can affect which checks the agent performs and which findings it reports.

## Scope

Use a local or authorized test site and synthetic inputs. The runner defaults to same-origin requests. If a site needs a trusted CDN for scripts, styles, images, fonts, or media, add its origin with `--asset-origin https://cdn.example.com`. Repeat the option for additional CDNs. The option does not authorize cross-origin page navigation or API requests. Use `--allow-youtube-embeds` for pages with a YouTube background or embedded video. This allows the embedded player and its media requests while keeping top-level YouTube links out of scope. Playwright still selects actions from the main page. Page snapshots go to the local Ollama service, and reports, screenshots, and recordings may contain test inputs or page content. The runner covers functional UI checks such as search and filters. Sign-in testing with managed test credentials is a future milestone.

Run `npm run check` to type-check the project.
