import { createRequire } from 'node:module'
import { mkdtempSync, rmSync, writeFileSync, readdirSync, readFileSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { fileURLToPath } from 'node:url'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
const repo=process.env.NOVALIST_REPO ?? process.cwd()
const work=process.env.NOVALIST_HELP_EVIDENCE_DIR ?? mkdtempSync(join(tmpdir(),'novalist-help-evidence-'))
const { _electron }=createRequire(join(repo,'app/package.json'))('@playwright/test')
const settings=mkdtempSync(join(tmpdir(),'nl-help-refresh-'))
const env=Object.fromEntries(Object.entries(process.env).filter(([k,v])=>v!==undefined&&k!=='ELECTRON_RUN_AS_NODE'))
Object.assign(env,{NOVALIST_SETTINGS_DIR:settings,NOVALIST_NO_SPLASH:'1'})
const sha=bytes=>createHash('sha256').update(bytes).digest('hex')
const expected=readdirSync(join(repo,'docs/manual')).filter(f=>f.endsWith('.md')).flatMap(file=>{
 const text=readFileSync(join(repo,'docs/manual',file),'utf8');const title=/^# (.+)$/m.exec(text)?.[1]
 return [...text.matchAll(/!\[([^\]]*)\]\(images\/([^)]+\.png)\)/g)].map(m=>({file,title,alt:m[1],image:m[2],sha256:sha(readFileSync(join(repo,'docs/manual/images',m[2])))}))
})
if(expected.length!==11)throw new Error('Expected all eleven manual screenshots')
const app=await _electron.launch({args:[join(repo,'app/out/main/index.js'),'--user-data-dir='+join(settings,'electron')],env})
const result={status:'running',source_revision:execFileSync('git',['rev-parse','HEAD'],{cwd:repo,encoding:'utf8'}).trim(),renderer_html_sha256:sha(readFileSync(join(repo,'app/out/renderer/index.html'))),manual_source_hashes:Object.fromEntries(readdirSync(join(repo,'docs/manual')).filter(f=>f.endsWith('.md')).map(f=>[f,sha(readFileSync(join(repo,'docs/manual',f)))])),count:0,pages:[],images:[],pageErrors:[],remoteRequests:[]}
try {
 const page=await app.firstWindow();page.setDefaultTimeout(30000)
 page.on('pageerror',e=>result.pageErrors.push(e.message))
 await page.route('**/*',route=>{
  const url=route.request().url()
  if(/^https?:/.test(url)){result.remoteRequests.push(url);return route.abort()}
  return route.continue()
 })
 await page.context().setOffline(true)
 await page.locator('.status-backend.connected').waitFor()
 await app.evaluate(({BrowserWindow})=>BrowserWindow.getAllWindows()[0].setBounds({x:40,y:40,width:1440,height:900}))
 await page.evaluate(()=>window.novalistStores.shell.getState().setHelpOpen(true))
 await page.locator('.help-card').waitFor()
 mkdirSync(join(work,'desktop-help'),{recursive:true})
 const titles=await page.locator('.help-page-list .help-result-page').allTextContents()
 if(titles.length!==47)throw new Error('Expected all47 manual pages; got '+titles.length)
 for(const title of titles){
  await page.locator('.help-page-list').getByRole('button',{name:title,exact:true}).click()
  await page.waitForFunction(title=>document.querySelector('.help-content h1')?.textContent?.trim()===title,title)
  result.pages.push(title)
  const pictures=await page.locator('.help-content .help-image').all()
  for(const image of pictures){
   const alt=await image.getAttribute('alt');const item=expected.find(item=>item.alt===alt)
   if(!item)throw new Error('Unexpected Help image: '+alt)
   await image.evaluate(el=>el.decode());await image.scrollIntoViewIfNeeded()
   const details=await image.evaluate(el=>({width:el.naturalWidth,height:el.naturalHeight,src:el.currentSrc}))
   if(details.width!==1440||details.height!==900)throw new Error('Unexpected refreshed image dimensions: '+item.image)
   if(!details.src.startsWith('file:'))throw new Error('Expected locally emitted screenshot asset')
   const bundledHash=sha(readFileSync(fileURLToPath(details.src)))
   if(bundledHash!==item.sha256)throw new Error('Bundled screenshot differs from current documentation: '+item.image)
   await page.screenshot({path:join(work,'desktop-help',item.image)})
   result.images.push({...item,width:details.width,height:details.height,bundled_sha256:bundledHash,local_emitted_asset:true});result.count++
  }
 }
 if(result.count!==11)throw new Error('Expected all11 decoded manual screenshots')
 if(result.pageErrors.length||result.remoteRequests.length)throw new Error('Help produced JavaScript errors or remote requests')
 result.status='passed';result.offline=true
}finally{writeFileSync(join(work,'help-verification.json'),JSON.stringify(result,null,2)+'\n');await app.close();rmSync(settings,{recursive:true,force:true})}
console.log(JSON.stringify({status:result.status,pages:result.pages.length,count:result.count,pageErrors:result.pageErrors.length,remoteRequests:result.remoteRequests.length}))
