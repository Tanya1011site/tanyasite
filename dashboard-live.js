/* Same-origin snapshots only. Public GitHub Pages stays in demo mode. */
(()=>{
 if(!window.liveMode)return;
 const loopback=['127.0.0.1','localhost','[::1]'].includes(location.hostname);
 const status=document.createElement('section');status.className='feed-status stale';status.setAttribute('role','status');document.querySelector('.order-alerts').before(status);
 const setText=(id,text)=>{document.getElementById(id).textContent=text};
 document.querySelector('.notice').textContent='兆豐本機行情連動｜僅顯示此工具實際收到的資料。單月合約、無歷史回補；手機請透過工具的 QR Code 配對。';
 document.querySelector('.mock-pill').textContent='真實行情';
 document.querySelector('header small').textContent='兆豐本機行情 · 真實資料';
 document.querySelector('.footer').textContent='此頁由本機工具提供。手機可透過工具的配對連結連入；GitHub Pages 仍為模擬行情。';
 if(window.remotePhoneMode){document.querySelector('.notice').textContent='兆豐手機真實行情｜電腦透過 HTTPS 通道同步；單月合約、無歷史回補。';document.querySelector('.footer').textContent='手機行情由你的電腦提供，電腦與行情工具需持續開啟。配對最長 8 小時有效。';document.querySelector('header small').textContent='兆豐手機同步 · 真實資料';}
 const oldSummary=document.querySelector('.order-alerts summary');oldSummary.firstChild.textContent='大口數成交通知 · 真實行情 ';
 document.querySelector('.order-alerts').setAttribute('aria-label','真實大口數成交通知');
 document.querySelector('.order-note').textContent='依每筆成交回呼口數觸發，不代表單一委託或新倉進場。僅期貨在有效近期買賣報價下推估主動買／賣，其餘方向未明。不提供鎖屏推播。';
 document.getElementById('order-test').hidden=true;
 document.querySelector('#order-direction option[value="both"]').textContent='全部（含方向未明）';
 document.querySelector('#order-direction option[value="bull"]').textContent='只看主動買推估';
 document.querySelector('#order-direction option[value="bear"]').textContent='只看主動賣推估';
 document.getElementById('pause').hidden=true;document.getElementById('refresh').hidden=true;
 for(const id of ['contract','filter','threshold'])document.getElementById(id).disabled=true;
 for(const id of ['price','mobile-price','change','mobile-change','call-total','put-total','ratio'])setText(id,'—');
 setText('mobile-contract','等待商品');setText('mobile-update','尚未收到行情');
 for(const card of document.querySelectorAll('main [data-view="chain"],main [data-view="flow"]')){
  const title=card.querySelector('h2')?.textContent||'主力集中';card.replaceChildren();const h=document.createElement('h2');h.textContent=title.replaceAll('模擬','');const p=document.createElement('p');p.className='padded muted';p.textContent='尚未接入：目前只訂閱單一商品的成交與買賣報價，不提供完整選擇權鏈、未平倉或主力籌碼。';card.append(h,p);
 }
 const chartCard=document.querySelector('.right [data-view="chart"]');chartCard.querySelector('h2').textContent='真實行情 K 線';chartCard.querySelector('.padded > small:last-child').textContent='依本機接收時間建立一分鐘 K 線，無成交分鐘不補線。MA144 需 144 根有成交 K 線；重啟後歷史清空。';
 document.getElementById('candles').setAttribute('aria-label','兆豐真實成交 K 線與均線');
 const notes=document.querySelectorAll('.right [data-view="chart"] .padded > small');notes[notes.length-1].textContent='依目前收到的成交區間計算回撤與轉折候選；不代表完整艾略特波浪判讀。';
 // Show a useful chart first while option-chain data is unavailable.
 document.querySelector('[data-tab="chart"]').click();
 let feed=null,lastSeq=0,inFlight=false;
 function offline(message){status.className='feed-status stale';status.textContent=message+'；保留最後資料，不再顯示為即時。';setText('status','行情未更新');setText('mobile-update','已離線／資料未更新');}
 async function poll(){if(inFlight)return;inFlight=true;try{
  const response=await fetch('/api/snapshot',{cache:'no-store',signal:AbortSignal.timeout(5000)});if(!response.ok)throw Error('HTTP '+response.status);const s=await response.json();
  if(!Array.isArray(s.candles)||!s.candles.every(c=>[c.time,c.open,c.high,c.low,c.close,c.volume].every(Number.isFinite)&&c.close>0&&c.high>=Math.max(c.open,c.close)&&c.low<=Math.min(c.open,c.close)))throw Error('行情格式無效');
  const session=String(s.feedId||'legacy');const sequence=Number(s.tradeSeq)||0;
  if(feed!==session){feed=session;lastSeq=sequence;window.clearOrderAlertHistory?.();}
  candles=s.candles;drawCandles();
  const price=candles.length?candles.at(-1).close:null;for(const id of ['price','mobile-price'])setText(id,price===null?'—':price.toLocaleString('zh-TW'));
  setText('mobile-contract',s.symbol||'等待商品');document.querySelector('.metric span').textContent=s.symbol||'等待訂閱商品';
  const option=document.createElement('option');option.textContent=s.symbol||'請在連接工具訂閱';document.getElementById('contract').replaceChildren(option);
  const last=Math.max(Number(s.lastReceipt)||0,Number(s.quoteReceipt)||0),stale=!last||Date.now()-last>60000;
  status.className='feed-status'+(!s.connected||stale?' stale':'');
  status.textContent=`${s.status||'等待行情'}｜商品 ${s.symbol||'—'}｜買價 ${s.quoteReceipt?s.bid:'—'}／賣價 ${s.quoteReceipt?s.ask:'—'}｜成交回呼 ${s.tradeCallbacks||0} 筆，報價回呼 ${s.bookCallbacks||0} 筆。${!s.connected?'已斷線。':stale?'目前沒有新資料；交易時段、訂閱與行情權限仍需確認。':''}`;
  const stamp=last?new Date(last).toLocaleTimeString('zh-TW',{hour12:false,timeZone:'Asia/Taipei'}):'尚無資料';
  setText('status',!s.connected?'兆豐已斷線':stale?'等待行情／資料未更新':'兆豐行情接收中');setText('mobile-update',stamp+(!s.connected?' · 已斷線':stale?' · 等待更新':''));setText('updated','最後接收 '+stamp);
  const missing=[5,10,20,60,144].filter(n=>candles.length<n);if(missing.length)setText('ohlc',document.getElementById('ohlc').textContent+'｜資料不足：'+missing.map(n=>'MA'+n).join('、'));
  if(Array.isArray(s.trades)){
   const incoming=s.trades.filter(t=>Number.isInteger(t.seq)&&t.seq>lastSeq).sort((a,b)=>a.seq-b.seq);
   if(incoming.length&&incoming[0].seq>lastSeq+1)status.textContent+='通知記錄有缺口，頁面離線期間部分成交未保留。';
   for(const t of incoming){if(s.connected&&Date.now()-t.receipt<=10000&&Date.now()-t.receipt>=0)window.receiveLiveTrade?.(t,s.symbol);}
  }
  lastSeq=Math.max(lastSeq,sequence);
 }catch(e){offline(e.message==='行情格式無效'?e.message:'無法連接本機行情工具');}finally{inFlight=false}}
 if(!loopback&&!window.remotePhoneMode){offline('真實行情模式需從 Windows 工具的「開啟看盤」進入，公開網站未接入行情');drawCandles();return;}
 window.refreshLiveDashboard=poll;poll();setInterval(poll,1000);
})();
