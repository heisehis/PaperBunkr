// Renders a post card from a spec: node social/templates/render.js <spec.json>
//   -> <out>.png (1200x675 at 2x, so 2400x1350)
//   -> with "clip" in the spec: <out>.gif and <out>.mp4, the clip playing inside the card's media slot.
// Same Playwright/Chromium the brag renders use (.claude/skills/paperbunkr-brag/scripts/render.js).
const path = require('path'), fs = require('fs');
const { spawnSync } = require('child_process');
const { chromium } = require('C:/Users/DeeDee/AppData/Roaming/npm/node_modules/@playwright/cli/node_modules/playwright');
const CHROME = 'C:/Users/DeeDee/AppData/Local/ms-playwright/chromium-1234/chrome-win64/chrome.exe';

const specPath = path.resolve(process.argv[2] || '');
if (!fs.existsSync(specPath)) { console.error('usage: node render.js <spec.json>'); process.exit(1); }
const spec = JSON.parse(fs.readFileSync(specPath, 'utf8'));
const specDir = path.dirname(specPath), repo = path.resolve(__dirname, '..', '..');
const fileUrl = p => 'file:///' + p.replace(/\\/g, '/');
const resolveLocal = p => path.isAbsolute(p) ? p : path.resolve(repo, p);

// Images: URLs as they are, local paths (repo-relative or absolute) as file URLs.
for (const [k, v] of Object.entries(spec.images || {})) {
  if (v && !/^https?:\/\//.test(v)) {
    const p = resolveLocal(v);
    if (!fs.existsSync(p)) { console.error(`image "${k}" not found: ${p}`); process.exit(1); }
    spec.images[k] = fileUrl(p);
  }
}
const out = resolveLocal(spec.out || path.join('social', 'media', path.basename(specPath, '.json')));
fs.mkdirSync(path.dirname(out), { recursive: true });

function ff(args) {
  const r = spawnSync('ffmpeg', ['-y', '-loglevel', 'error', ...args], { stdio: 'inherit' });
  if (r.status !== 0) { console.error('ffmpeg failed'); process.exit(1); }
}

(async () => {
  const browser = await chromium.launch({ executablePath: CHROME, args: ['--allow-file-access-from-files'] });
  const shoot = async (scale, file) => {
    const page = await browser.newPage({ viewport: { width: 1200, height: 675 }, deviceScaleFactor: scale });
    page.on('pageerror', e => console.error('PAGE ERROR', e.message));
    await page.goto(fileUrl(path.join(__dirname, 'card.html')));
    await page.evaluate(() => window.ready);
    const boxes = await page.evaluate(s => window.render(s), spec);
    await page.screenshot({ path: file, clip: { x: 0, y: 0, width: 1200, height: 675 } });
    await page.close();
    return boxes;
  };

  await shoot(spec.scale || 2, out + '.png');
  console.log('wrote ' + out + '.png');

  if (spec.clip) {
    // The card at 1x is the background; the clip is cropped, cover-scaled into the media slot and laid over it.
    const bg = out + '.bg.png';
    const boxes = await shoot(1, bg);
    if (!boxes.media) { console.error('this layout has no media slot for a clip'); process.exit(1); }
    const [x, y, w, h] = boxes.media.map(Math.round);
    const c = spec.clip, fps = c.fps || 12, width = c.width || 960;
    const src = resolveLocal(c.src);
    const clipChain = `[0:v]${c.crop ? `crop=${c.crop},` : ''}fps=${fps},scale=${w}:${h}:force_original_aspect_ratio=increase,crop=${w}:${h},setsar=1[c];` +
                      `[1:v][c]overlay=${x}:${y}:shortest=1,scale=${width}:-2:flags=lanczos`;
    const input = ['-ss', String(c.start || 0), '-t', String(c.duration || 6), '-i', src, '-loop', '1', '-framerate', String(fps), '-i', bg];
    ff([...input, '-filter_complex', `${clipChain},split[a][b];[a]palettegen=max_colors=${c.colors || 160}:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle`, out + '.gif']);
    ff([...input, '-filter_complex', clipChain, '-an', '-c:v', 'libx264', '-crf', '20', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', out + '.mp4']);
    // The PNG becomes a poster: the 2x card with a frame from the middle of the clip in the slot.
    const s = spec.scale || 2, mid = (c.start || 0) + (c.duration || 6) * (c.posterAt ?? 0.5);
    ff(['-ss', String(mid), '-i', src, '-i', out + '.png', '-filter_complex',
        `[0:v]${c.crop ? `crop=${c.crop},` : ''}scale=${w * s}:${h * s}:force_original_aspect_ratio=increase,crop=${w * s}:${h * s},setsar=1[c];[1:v][c]overlay=${x * s}:${y * s}`,
        '-frames:v', '1', out + '.poster.png']);
    fs.renameSync(out + '.poster.png', out + '.png');
    fs.unlinkSync(bg);
    for (const ext of ['.gif', '.mp4']) console.log(`wrote ${out}${ext} (${(fs.statSync(out + ext).size / 1048576).toFixed(1)} MB)`);
  }
  await browser.close();
})();
