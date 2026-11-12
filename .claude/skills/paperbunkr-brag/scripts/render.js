// node render.js A stills 1.5 2.9 ...  -> stills/A_<t>.jpg
// node render.js A video               -> video_A_noaudio.mp4
const path = require('path'), fs = require('fs');
const { spawn } = require('child_process');
const { chromium } = require('C:/Users/DeeDee/AppData/Roaming/npm/node_modules/@playwright/cli/node_modules/playwright');
const FPS = 30, here = __dirname;

(async () => {
  let [tl, mode, ...rest] = process.argv.slice(2);
  const V = rest[0] === 'v' || rest[0] === 'h' ? rest.shift() === 'v' : false;
  const W = V ? 1080 : 1920, H = V ? 1920 : 1080, tag = tl + (V ? '_v' : '');
  const browser = await chromium.launch({ executablePath: 'C:/Users/DeeDee/AppData/Local/ms-playwright/chromium-1234/chrome-win64/chrome.exe', args: ['--allow-file-access-from-files'] });
  const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
  page.on('pageerror', e => console.error('PAGE ERROR', e.message));
  await page.goto('file:///' + path.join(here, V ? 'vert.html' : 'comp.html').replace(/\\/g, '/') + '?tl=' + tl + (V ? '&fmt=v' : '') + (process.env.HANDLE ? '&handle=' + encodeURIComponent(process.env.HANDLE) : ''));
  await page.evaluate(() => window.ready);
  const dur = await page.evaluate(() => window.TL.dur);
  const stage = await page.$('#stage');
  if (mode === 'stills') {
    fs.mkdirSync(path.join(here, 'stills'), { recursive: true });
    for (const s of rest) {
      await page.evaluate(t => renderAt(t), parseFloat(s));
      await stage.screenshot({ path: path.join(here, 'stills', `${tag}_${s}.jpg`), type: 'jpeg', quality: 85 });
    }
  } else {
    const out = path.join(here, `video_${tag}_noaudio.mp4`);
    const ff = spawn('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'image2pipe', '-framerate', String(FPS), '-c:v', 'mjpeg', '-i', '-',
      '-c:v', 'libx264', '-preset', 'medium', '-crf', '16', '-pix_fmt', 'yuv420p', '-r', String(FPS), out], { stdio: ['pipe', 'inherit', 'inherit'] });
    const total = Math.round(dur * FPS);
    for (let f = 0; f < total; f++) {
      await page.evaluate(t => renderAt(t), f / FPS);
      const buf = await page.screenshot({ type: 'jpeg', quality: 93, clip: { x: 0, y: 0, width: W, height: H }, timeout: 180000 });
      if (!ff.stdin.write(buf)) await new Promise(r => ff.stdin.once('drain', r));
      if (f % 150 === 0) console.log(`${tl} frame ${f}/${total}`);
    }
    ff.stdin.end(); await new Promise(r => ff.on('close', r));
    console.log('wrote', out);
  }
  await browser.close();
})().catch(e => { console.error(e); process.exit(1); });
