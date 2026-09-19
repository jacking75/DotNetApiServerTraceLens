#!/usr/bin/env node
/**
 * 여섯 보기와 대화상자를 헤드리스 Chrome으로 찍는다.
 *
 * 자동 판정은 하지 않는다 — "안 보이는 글자"는 사람이 봐야 안다.
 * 이 스크립트의 목적은 라이트/다크, 1600×950/1366×768을 매번 같은 방식으로 찍어
 * 고치기 전후를 나란히 놓을 수 있게 하는 것이다.
 *
 * 쓰는 법:
 *   1. 다른 터미널에서 `tracelens serve --target samples/OrderApi --port 25099`
 *   2. `npm run screenshots -- --theme light --port 25099`
 *
 * 로컬에 설치된 Chrome을 쓴다(puppeteer-core는 브라우저를 내려받지 않는다).
 * 찾지 못하면 `--chrome <경로>`로 알려 준다.
 */
import { existsSync, mkdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import puppeteer from 'puppeteer-core'

const here = dirname(fileURLToPath(import.meta.url))
const repoRoot = resolve(here, '..', '..')

const chromeCandidates = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
]

/** 여섯 보기 + 대화상자. 해시로 열 수 있는 것은 해시로 연다(T45가 그것을 가능하게 했다). */
const shots = [
  { name: 'callflow', hash: '#view=callflow' },
  { name: 'classmap', hash: '#view=classmap' },
  { name: 'flowmap', hash: '#view=flowmap' },
  { name: 'startup', hash: '#view=startup' },
  { name: 'data-route', hash: '#view=data&sub=route' },
  { name: 'data-comp', hash: '#view=data&sub=comp' },
  { name: 'data-check', hash: '#view=data&sub=check' },
  { name: 'middleware', hash: '#view=middleware&sub=pipe' },
]

function parseArguments(argv) {
  const options = { theme: 'dark', port: 25099, width: 1600, height: 950, chrome: '' }
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index]
    if (!key.startsWith('--')) continue
    const value = argv[index + 1]
    if (key === '--theme') options.theme = value
    if (key === '--port') options.port = Number(value)
    if (key === '--width') options.width = Number(value)
    if (key === '--height') options.height = Number(value)
    if (key === '--chrome') options.chrome = value
  }

  return options
}

async function main() {
  const options = parseArguments(process.argv.slice(2))
  const executablePath = options.chrome || chromeCandidates.find((path) => existsSync(path))
  if (!executablePath) {
    console.error('Chrome을 찾지 못했다. --chrome <경로>로 알려 준다.')
    process.exitCode = 2
    return
  }

  const outputDirectory = resolve(repoRoot, 'docs', 'screenshots', options.theme)
  mkdirSync(outputDirectory, { recursive: true })

  const browser = await puppeteer.launch({
    executablePath,
    headless: 'new',
    args: ['--force-device-scale-factor=1'],
  })

  try {
    const page = await browser.newPage()
    await page.setViewport({ width: options.width, height: options.height })
    const base = `http://127.0.0.1:${options.port}/`

    // 테마·언어는 localStorage에 있다. 첫 로드 전에 넣어야 첫 화면부터 그 테마로 뜬다.
    await page.goto(base, { waitUntil: 'domcontentloaded' })
    await page.evaluate((theme) => {
      const raw = window.localStorage.getItem('tracelens.view')
      const state = raw ? JSON.parse(raw) : {}
      window.localStorage.setItem('tracelens.view', JSON.stringify({ ...state, theme }))
    }, options.theme)

    for (const shot of shots) {
      await page.goto(`${base}${shot.hash}`, { waitUntil: 'networkidle2' })
      // 캔버스 배치가 끝나기를 기다린다. 고정 대기 대신 한 프레임 뒤를 본다.
      await page.evaluate(() => new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done))))
      const file = resolve(outputDirectory, `${shot.name}-${options.width}x${options.height}.png`)
      await page.screenshot({ path: file })
      console.log(file)
    }
  } finally {
    await browser.close()
  }
}

await main()
