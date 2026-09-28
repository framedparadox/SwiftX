# SwiftX website

The marketing site for SwiftX: a single static page plus privacy, terms and 404 pages. Plain HTML,
CSS and JavaScript: no framework, no build step, no third-party requests.

```
website/
  index.html            The product page
  privacy.html          Privacy policy (app and website)
  terms.html            Terms and conditions, mirrored from ../TERMS.md
  404.html              Served by GitHub Pages for any missing path
  assets/css/site.css   All styles
  assets/js/site.js     The live orbit, the demos, nav and scroll behaviour
  assets/fonts/         Inter, self-hosted (SIL OFL 1.1)
  assets/img/           Mark, icons, share image, app screenshots
```

## Preview locally

```bash
cd website
python -m http.server 8000     # then open http://localhost:8000
```

## What's live on the page

The orbit on the page is a small web port of the real one (`site.js`, `class Orbit`): eight slots
45° apart from straight up with Settings and Close pinned last, groups opening a concentric outer
orbit after a 260 ms dwell, labels on the item's own radial, and the corner quadrant growing its
radius only as far as it must. The mark is drawn from the same geometry as `Services/AppLogo.cs`.

- **Ctrl+Shift+X** anywhere on the page summons the orbit; **Esc** closes a group, then the orbit.
- The **Customize** section drives the orbit's size, shape, radius, hover style and theme live.
- The **Hot corner** section fires after a 250 ms dwell in the chosen corner, re-arms on leave, and
  ignores a held mouse button, as `HotCornerWatcher` does.
- The **Data formatter / Base64** demo runs entirely in the browser.

## Deploying

`.github/workflows/pages.yml` publishes this folder to GitHub Pages on every push to `main` that
touches `website/`, and can be run by hand from the Actions tab. Enable it once under
**Settings → Pages → Build and deployment → Source: GitHub Actions**.

The site is written for `https://framedparadox.github.io/SwiftX/`. On a custom domain, update the
canonical/Open Graph URLs in each page's `<head>`, `robots.txt`, `sitemap.xml`, and the
`<base href>` in `404.html`. If it is hosted somewhere other than GitHub Pages, update the
"This website" section of `privacy.html` too.

## Keeping it in step with the app

- Version, requirements and feature facts come from the root `README.md`; the release link always
  points at `releases/latest`.
- Privacy copy mirrors the Privacy page in the app (`ShellWindow.xaml`) and `IconService.cs`'s
  icon fetch, including its DuckDuckGo fallback.
- `terms.html` mirrors `../TERMS.md`. Change both together.
- Screenshots in `assets/img/screens/` are copies of `../img/SwiftX2_*.png`.
- `assets/img/*.png` icons and `og.png` were rendered from `mark.svg` and the page itself in
  headless Chromium.
