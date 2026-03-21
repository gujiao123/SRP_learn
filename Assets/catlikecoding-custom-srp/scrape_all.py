"""
Scraper for Catlike Coding Custom SRP tutorial series.
Saves each tutorial as a markdown file in its own folder.
"""
import urllib.request
import time
import os
import re

# All 17 tutorials
tutorials = [
    ("01-custom-render-pipeline", "https://catlikecoding.com/unity/tutorials/custom-srp/custom-render-pipeline/"),
    ("02-draw-calls", "https://catlikecoding.com/unity/tutorials/custom-srp/draw-calls/"),
    ("03-directional-lights", "https://catlikecoding.com/unity/tutorials/custom-srp/directional-lights/"),
    ("04-directional-shadows", "https://catlikecoding.com/unity/tutorials/custom-srp/directional-shadows/"),
    ("05-baked-light", "https://catlikecoding.com/unity/tutorials/custom-srp/baked-light/"),
    ("06-shadow-masks", "https://catlikecoding.com/unity/tutorials/custom-srp/shadow-masks/"),
    ("07-lod-and-reflections", "https://catlikecoding.com/unity/tutorials/custom-srp/lod-and-reflections/"),
    ("08-complex-maps", "https://catlikecoding.com/unity/tutorials/custom-srp/complex-maps/"),
    ("09-point-and-spot-lights", "https://catlikecoding.com/unity/tutorials/custom-srp/point-and-spot-lights/"),
    ("10-point-and-spot-shadows", "https://catlikecoding.com/unity/tutorials/custom-srp/point-and-spot-shadows/"),
    ("11-post-processing", "https://catlikecoding.com/unity/tutorials/custom-srp/post-processing/"),
    ("12-hdr", "https://catlikecoding.com/unity/tutorials/custom-srp/hdr/"),
    ("13-color-grading", "https://catlikecoding.com/unity/tutorials/custom-srp/color-grading/"),
    ("14-multiple-cameras", "https://catlikecoding.com/unity/tutorials/custom-srp/multiple-cameras/"),
    ("15-particles", "https://catlikecoding.com/unity/tutorials/custom-srp/particles/"),
    ("16-render-scale", "https://catlikecoding.com/unity/tutorials/custom-srp/render-scale/"),
    ("17-fxaa", "https://catlikecoding.com/unity/tutorials/custom-srp/fxaa/"),
]

BASE_DIR = r"f:\各种实验合集\catlikecoding-custom-srp"

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
    "Accept": "text/html,application/xhtml+xml",
    "Accept-Language": "en-US,en;q=0.9",
}

def fetch_html(url):
    req = urllib.request.Request(url, headers=HEADERS)
    with urllib.request.urlopen(req, timeout=30) as resp:
        return resp.read().decode("utf-8", errors="ignore")

def html_to_text(html, url):
    """Extract article content using trafilatura if available, else fallback."""
    try:
        import trafilatura
        result = trafilatura.extract(html, include_links=True, include_tables=True, output_format="markdown")
        if result:
            return result
    except ImportError:
        pass

    # Fallback: basic BeautifulSoup extraction
    try:
        from bs4 import BeautifulSoup
        soup = BeautifulSoup(html, "html.parser")
        # Remove nav, footer, aside noise
        for tag in soup(["script", "style", "nav", "footer", "aside", "header"]):
            tag.decompose()
        # Try to find main article content
        article = soup.find("article") or soup.find("main") or soup.find("section") or soup.body
        if article:
            # Process code blocks
            for code in article.find_all("code"):
                lang = code.get("class", [""])[0].replace("language-", "") if code.get("class") else ""
                code.replace_with(f"\n```{lang}\n{code.get_text()}\n```\n")
            # Process headings
            for i in range(1, 7):
                for h in article.find_all(f"h{i}"):
                    h.replace_with(f"\n{'#' * i} {h.get_text(strip=True)}\n")
            # Process paragraphs and divs
            text = article.get_text(separator="\n", strip=True)
            # Clean up excessive blank lines
            text = re.sub(r"\n{3,}", "\n\n", text)
            return text
    except ImportError:
        # Pure regex fallback
        html_clean = re.sub(r"<script[^>]*>.*?</script>", "", html, flags=re.DOTALL | re.IGNORECASE)
        html_clean = re.sub(r"<style[^>]*>.*?</style>", "", html_clean, flags=re.DOTALL | re.IGNORECASE)
        html_clean = re.sub(r"<[^>]+>", " ", html_clean)
        text = re.sub(r"&nbsp;", " ", html_clean)
        text = re.sub(r"&lt;", "<", text)
        text = re.sub(r"&gt;", ">", text)
        text = re.sub(r"&amp;", "&", text)
        text = re.sub(r"\s{3,}", "\n\n", text)
        return text.strip()
    return ""

def extract_title(html):
    m = re.search(r"<h1[^>]*>(.*?)</h1>", html, re.IGNORECASE | re.DOTALL)
    if m:
        return re.sub(r"<[^>]+>", "", m.group(1)).strip()
    m = re.search(r"<title>(.*?)</title>", html, re.IGNORECASE)
    if m:
        return m.group(1).strip()
    return "Tutorial"

def main():
    # Try to install trafilatura if not present
    try:
        import trafilatura
        print("trafilatura available, using for best quality extraction")
    except ImportError:
        print("trafilatura not found, trying to install...")
        import subprocess
        subprocess.run(["uv", "pip", "install", "trafilatura"], check=False)
        try:
            import trafilatura
            print("trafilatura installed successfully")
        except ImportError:
            try:
                from bs4 import BeautifulSoup
                print("Using BeautifulSoup fallback")
            except ImportError:
                print("Using basic regex fallback")

    for folder_name, url in tutorials:
        folder = os.path.join(BASE_DIR, folder_name)
        os.makedirs(folder, exist_ok=True)
        out_file = os.path.join(folder, "tutorial.md")

        if os.path.exists(out_file) and os.path.getsize(out_file) > 1000:
            print(f"[SKIP] {folder_name} already scraped")
            continue

        print(f"[FETCH] {folder_name} ...")
        try:
            html = fetch_html(url)
            title = extract_title(html)
            content = html_to_text(html, url)
            
            header = f"# {title}\n\n> Source: {url}\n\n---\n\n"
            with open(out_file, "w", encoding="utf-8") as f:
                f.write(header + content)
            
            size_kb = os.path.getsize(out_file) / 1024
            print(f"[OK]   {folder_name} -> {size_kb:.1f} KB")
        except Exception as e:
            print(f"[ERR]  {folder_name}: {e}")
        
        time.sleep(1.5)  # polite delay

    print("\nDone! All tutorials saved.")

if __name__ == "__main__":
    main()
