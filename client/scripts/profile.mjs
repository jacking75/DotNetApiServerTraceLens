#!/usr/bin/env node
/**
 * 매일 하는 조작 세 가지가 React 커밋을 몇 번 일으키는지 센다(T67).
 *
 * "빨라졌다"를 측정 없이 쓰지 않기 위한 스크립트다. 커밋 수는 React DevTools 훅의
 * `onCommitFiberRoot` 호출 횟수이고, 요청 수는 브라우저가 실제로 보낸 HTTP 요청이다.
 * 둘 다 사람이 세는 것이 아니라 브라우저가 세므로 같은 방식으로 다시 잴 수 있다.
 *
 * 쓰는 법:
 *   1. 다른 터미널에서 `tracelens serve --target samples/OrderApi --port 25099 --no-browser`
 *   2. `npm run profile -- --port 25099`
 *
 * 로컬에 설치된 Chrome을 쓴다(puppeteer-core는 브라우저를 내려받지 않는다).
 */
import { existsSync } from 'node:fs'
import puppeteer from 'puppeteer-core'

const chromeCandidates = [
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  'C:/Program Files/Microsoft/Edge/Application/msedge.exe',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
]

function parseArguments(argv) {
  const options = { port: 25099, width: 1600, height: 950, chrome: '' }
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index]
    if (!key.startsWith('--')) continue
    const value = argv[index + 1]
    if (key === '--port') options.port = Number(value)
    if (key === '--width') options.width = Number(value)
    if (key === '--height') options.height = Number(value)
    if (key === '--chrome') options.chrome = value
  }

  return options
}

/**
 * React가 주입하는 DevTools 훅 자리에 계수기만 있는 가짜를 먼저 넣는다.
 * 개발 빌드가 아니어도 React는 이 훅이 있으면 커밋마다 `onCommitFiberRoot`를 부른다.
 */
const installCounter = () => {
  window.__tlCommits = 0
  let nextId = 1
  window.__REACT_DEVTOOLS_GLOBAL_HOOK__ = {
    renderers: new Map(),
    supportsFiber: true,
    isDisabled: false,
    checkDCE() {},
    inject(renderer) {
      const id = nextId
      nextId += 1
      this.renderers.set(id, renderer)
      return id
    },
    onCommitFiberRoot() {
      window.__tlCommits += 1
    },
    onPostCommitFiberRoot() {},
    onCommitFiberUnmount() {},
  }
}

async function measure(page, label, action) {
  const requests = []
  const listener = (request) => {
    if (request.url().includes('/api/')) requests.push(new URL(request.url()).pathname)
  }

  page.on('request', listener)
  await page.evaluate(() => { window.__tlCommits = 0 })
  const started = Date.now()
  await action()
  // 커밋이 끝나기를 기다린다. 두 프레임이면 이번 상호작용의 커밋은 모두 끝난다.
  await page.evaluate(() => new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done))))
  const elapsed = Date.now() - started
  const commits = await page.evaluate(() => window.__tlCommits)
  page.off('request', listener)

  console.log(`${label}: 커밋 ${commits}회 · API 요청 ${requests.length}건 · ${elapsed}ms`)
  if (requests.length > 0) console.log(`  요청: ${[...new Set(requests)].join(', ')}`)
  return { label, commits, requests: requests.length, elapsed }
}

async function main() {
  const options = parseArguments(process.argv.slice(2))
  const executablePath = options.chrome || chromeCandidates.find((path) => existsSync(path))
  if (!executablePath) {
    console.error('Chrome을 찾지 못했다. --chrome <경로>로 알려 준다.')
    process.exitCode = 2
    return
  }

  const browser = await puppeteer.launch({ executablePath, headless: 'new' })
  try {
    const page = await browser.newPage()
    await page.setViewport({ width: options.width, height: options.height })
    await page.evaluateOnNewDocument(installCounter)

    const base = `http://127.0.0.1:${options.port}/`
    await page.goto(`${base}#view=callflow`, { waitUntil: 'networkidle2' })
    await page.waitForSelector('.splitter', { timeout: 15000 })

    const results = []

    // 1) 스플리터 드래그 1초. 드래그 중에는 CSS 변수만 바꾸고 놓을 때 한 번만 state를 바꾼다.
    const splitter = await page.$('.splitter')
    const box = await splitter.boundingBox()
    results.push(await measure(page, '스플리터 드래그(20스텝)', async () => {
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
      await page.mouse.down()
      for (let step = 1; step <= 20; step += 1) {
        await page.mouse.move(box.x + box.width / 2 + step * 4, box.y + box.height / 2)
      }

      await page.mouse.up()
    }))

    // 2) 진입점 하나 고르기. 접힌 그룹이면 먼저 편다.
    const group = await page.$('.side-group')
    if (group) await group.click()
    const entry = await page.waitForSelector('.side-item', { timeout: 5000 }).catch(() => null)
    if (entry) {
      results.push(await measure(page, '진입점 선택', async () => {
        await entry.click()
        await page.evaluate(() => new Promise((done) => setTimeout(done, 400)))
      }))
    }

    // 3) 질문 타이핑. 문맥 요청이 타이핑마다 나가면 안 된다.
    const tabLabels = await page.$$eval('.right-tabs button', (nodes) => nodes.map((node) => node.textContent))
    const askTab = (await page.$$('.right-tabs button')).at(-1)
    if (!askTab) console.warn(`질문 탭을 찾지 못해 건너뛴다. 탭: ${tabLabels.join(' / ')}`)
    if (askTab) {
      await askTab.click()
      const askInput = '#tracelens-ask-panel textarea'
      const textarea = await page.waitForSelector(askInput, { timeout: 8000 }).catch(() => null)
      if (!textarea) console.warn('질문 입력칸을 찾지 못해 타이핑 측정을 건너뛴다.')
      if (textarea) {
        // 문맥을 먼저 받아 둔다. 타이핑 구간에 첫 문맥 요청이 섞이면 무엇을 센 것인지 알 수 없다.
        await page.evaluate(() => new Promise((done) => setTimeout(done, 1500)))
        const typed = '이 엔드포인트는 무엇을 하나'
        results.push(await measure(page, `질문 ${typed.length}자 타이핑`, async () => {
          await textarea.click()
          await page.keyboard.type(typed, { delay: 30 })
          await page.evaluate(() => new Promise((done) => setTimeout(done, 800)))
        }))
        // 실제로 입력됐는지 확인한다. 빈 칸에 친 것을 "요청이 없었다"로 읽으면 안 된다.
        const value = await page.$eval(askInput, (node) => node.value)
        if (value !== typed) console.warn(`입력이 들어가지 않았다(입력칸 값: "${value}").`)
      }
    }

    console.log('\n측정 조건: ' + `${options.width}x${options.height}, ` +
      `${base}, headless Chrome(${executablePath})`)
    console.log(JSON.stringify(results))
  } finally {
    await browser.close()
  }
}

await main()
