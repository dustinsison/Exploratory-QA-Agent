import { createServer } from 'node:http';

createServer((_request, response) => {
  response.setHeader('Content-Type', 'text/html; charset=utf-8');
  response.end(`<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Trail Supply</title></head>
<body>
<main>
<h1>Trail Supply</h1>
<form id="search">
<label>Search products <input name="query" type="search"></label>
<button>Search</button>
</form>
<label>Size <select id="size"><option value="">All sizes</option><option>8</option><option>9</option><option>12</option></select></label>
<button id="clear">Clear filters</button>
<p role="status"></p>
<ul id="results"></ul>
</main>
<script>
const products = [{name:'Summit hiking boots',size:'8'},{name:'Ridge hiking boots',size:'9'},{name:'Camp sandals',size:'8'}];
const search = document.querySelector('#search');
const size = document.querySelector('#size');
const results = document.querySelector('#results');
function render() {
  const query = search.elements.query.value.trim().toLowerCase();
  const matches = products.filter(product => product.name.toLowerCase().includes(query)
    && (!size.value || product.size === size.value));
  document.querySelector('[role=status]').textContent = matches.length
    ? matches.length + ' products found' : 'No products match';
  results.replaceChildren(...matches.map(product => {
    const item = document.createElement('li');
    item.textContent = product.name + ' (size ' + product.size + ')';
    return item;
  }));
}
search.addEventListener('submit', event => { event.preventDefault(); render(); });
size.addEventListener('change', render);
// Intentional demo defect: the selected size survives Clear filters.
document.querySelector('#clear').addEventListener('click', render);
render();
</script></body></html>`);
}).listen(3000, '127.0.0.1', () => console.log('Storefront at http://127.0.0.1:3000'));
