# Exploratory QA Agent

Give the agent a URL and a plain-English charter. It uses an OpenAI model to choose actions from the browser’s accessibility snapshot, executes them with Playwright, and saves a Markdown report with the recorded steps and a final screenshot.

## Run the storefront demo

Use Node.js 22 or newer. Install dependencies and Chromium, then start the local storefront.

```bash
npm ci
npx playwright install chromium
npm run demo
```

Set `OPENAI_API_KEY` in your environment, then run this in a second terminal.

```bash
npm run qa -- --url http://127.0.0.1:3000 \
  --charter "Search for hiking boots. Select size 12. Verify the empty state, then clear filters and check that matching products return."
```

The storefront contains a seeded defect. **Clear filters** leaves size 12 selected, so the two matching boots stay hidden. Check the generated report under `artifacts/` for the observed behavior and reproduction steps.

## Scope

Use a local or authorized test site and synthetic inputs. The runner allows requests to the starting origin, which can limit sites that use external assets or identity providers. Page accessibility snapshots are sent to the configured OpenAI model. Reports and screenshots may contain test inputs or page content. The initial runner covers functional UI checks such as search and filters. Sign-in testing with managed test credentials is a future milestone.

Run `npm run check` to type-check the project.
