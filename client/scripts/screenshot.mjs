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
  { name: 'callflow', hash: '#view=callflow', pickEntry: true },
  { name: 'ask', hash: '#view=callflow', pickEntry: true, tab: 'ask', height: 1300 },
  { name: 'classmap', hash: '#view=classmap' },
  { name: 'flowmap', hash: '#view=flowmap' },
  { name: 'startup', hash: '#view=startup' },
  { name: 'data-route', hash: '#view=data&sub=route' },
  { name: 'data-comp', hash: '#view=data&sub=comp' },
  { name: 'data-check', hash: '#view=data&sub=check' },
  { name: 'middleware', hash: '#view=middleware&sub=pipe' },
]

/** 진입점 목록에서 `POST /Orders`(컨트롤러 라우트)를 눌러 호출 흐름을 연다. 해시의 entry 값은 분석 때마다 달라질 수 있어 화면에서 고른다. */
async function pickSampleEntry(page) {
  const clicked = await page.evaluate(() => {
    const rows = [...document.querySelectorAll('button, li, [role="option"], [role="button"]')]
    const row = rows.find((element) => /^POST\s*\/Orders$/.test((element.textContent ?? '').trim()))
    if (!row) return false
    row.click()
    return true
  })
  if (!clicked) console.error('진입점 POST /Orders를 목록에서 찾지 못했다.')
  await new Promise((done) => setTimeout(done, 1500))
}

/** 오른쪽 패널의 탭(`ask`·`impact`)을 연다. 라벨은 언어마다 달라 i18n 사전의 두 언어 값을 모두 허용한다. */
async function openRightTab(page, tab) {
  const labels = tabLabels[tab]
  await page.evaluate((names) => {
    const target = [...document.querySelectorAll('button, [role="tab"]')]
      .find((element) => names.includes((element.textContent ?? '').trim()))
    target?.click()
  }, labels)
  await new Promise((done) => setTimeout(done, 800))
}

const tabLabels = { detail: ['Details', '상세'], ask: ['Ask', 'AI에게 질문', '질문'], impact: ['Impact', '영향', '변경 영향'] }

function parseArguments(argv) {
  const options = { theme: 'dark', port: 25099, width: 1600, height: 950, chrome: '', lang: '', out: '' }
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index]
    if (!key.startsWith('--')) continue
    const value = argv[index + 1]
    if (key === '--theme') options.theme = value
    if (key === '--port') options.port = Number(value)
    if (key === '--width') options.width = Number(value)
    if (key === '--height') options.height = Number(value)
    if (key === '--chrome') options.chrome = value
    if (key === '--lang') options.lang = value
    if (key === '--out') options.out = value
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

  const outputDirectory = options.out ? resolve(options.out) : resolve(repoRoot, 'docs', 'screenshots', options.theme)
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
      window.localStorage.setItem('tracelens.view', JSON.stringify({ ...state, theme, guide: { seen: true, completed: [] } }))
    }, options.theme)
    if (options.lang) {
      await page.evaluate((lang) => window.localStorage.setItem('tracelens.lang', lang), options.lang)
    }
    // 저장값은 첫 로드 때만 읽는다. 다시 불러와야 테마·언어·안내 닫힘이 적용된다.
    await page.reload({ waitUntil: 'domcontentloaded' })

    for (const shot of shots) {
      await page.goto('about:blank')
      await page.goto(`${base}${typeof shot.hash === 'function' ? shot.hash() : shot.hash}`, { waitUntil: 'networkidle2' })
      if (shot.pickEntry) await pickSampleEntry(page)
      // 오른쪽 탭은 저장되므로 앞 화면의 탭이 다음 화면으로 새지 않게 매번 정한다.
      await page.setViewport({ width: options.width, height: shot.height ?? options.height })
      await openRightTab(page, shot.tab ?? 'detail')
      // 캔버스 배치가 끝나기를 기다린다. 고정 대기 대신 한 프레임 뒤를 본다.
      await page.evaluate(() => new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done))))
      const file = resolve(outputDirectory, `${shot.name}-${options.width}x${shot.height ?? options.height}.png`)
      await page.screenshot({ path: file })
      console.log(file)
    }
  } finally {
    await browser.close()
  }
}

await main()
