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
 document.querySelector('.order-note').textContent='依每筆成交回呼口數觸發，不代表單一委託或新倉進場。期貨與選擇權依有效近期買賣報價推估主動方，其餘方向未明。不提供鎖屏推播。';
 document.getElementById('order-test').hidden=true;
 document.querySelector('#order-direction option[value="both"]').textContent='全部（含方向未明）';
 document.querySelector('#order-direction option[value="bull"]').textContent='只看主動買推估';
 document.querySelector('#order-direction option[value="bear"]').textContent='只看主動賣推估';
 document.getElementById('pause').hidden=true;document.getElementById('refresh').hidden=true;
 for(const id of ['contract','filter','threshold'])document.getElementById(id).disabled=true;
 for(const id of ['price','mobile-price','change','mobile-change','call-total','put-total','ratio'])setText(id,'—');
 setText('mobile-contract','等待商品');setText('mobile-update','尚未收到行情');
 for(const card of document.querySelectorAll('main [data-view="chain"],main [data-view="flow"]')){
  const title=card.querySelector('h2')?.textContent||'主力集中';card.replaceChildren();const h=document.createElement('h2');h.textContent=title.replaceAll('模擬','');const p=document.createElement('p');p.className='padded muted';p.textContent='尚未接入：報價與成交不包含未平倉、主力身份或大戶／散戶分組資料。';card.append(h,p);
 }
 window.RealFlow?.init();
 const chainCard=document.querySelector('main [data-view="chain"]');
 chainCard.innerHTML='<h2>選擇權即時行情 <small id="live-option-series"></small></h2><p id="live-option-state" class="padded muted">等待期貨行情後訂閱所選選擇權組別。</p><div class="table-wrap"><table><thead><tr><th>CALL 買／賣</th><th>CALL 成交</th><th>履約價</th><th>PUT 成交</th><th>PUT 買／賣</th></tr></thead><tbody id="live-option-chain"></tbody></table></div><p class="padded muted" id="live-option-detail">點選履約價查看商品代碼。本次成交量僅累計工具啟動後收到的成交，不是未平倉。</p>';
 const metricLabels=document.querySelectorAll('.metric span');metricLabels[2].textContent='本次 CALL 成交口數';metricLabels[3].textContent='本次 PUT 成交口數';metricLabels[4].textContent='選擇權報價接收';
 let selectedStrike=null,userSelectedView=false,autoViewSet=false,optionPairs=[];
 document.querySelectorAll('[data-tab]').forEach(b=>b.addEventListener('click',e=>{if(e.isTrusted)userSelectedView=true}));
 document.getElementById('live-option-chain').onclick=e=>{const row=e.target.closest('tr');if(!row)return;selectedStrike=Number(row.dataset.strike);showOptionDetail();};
 function showOptionDetail(){const pair=optionPairs.find(p=>p.strike===selectedStrike);if(!pair)return;for(const row of document.querySelectorAll('#live-option-chain tr'))row.classList.toggle('selected',Number(row.dataset.strike)===selectedStrike);setText('live-option-detail',`${pair.strike.toLocaleString('zh-TW')}｜CALL ${pair.call?.symbol||'—'}｜PUT ${pair.put?.symbol||'—'}｜本次接收成交量：CALL ${pair.call?.volume||0}／PUT ${pair.put?.volume||0} 口；未平倉尚未接入。`);}
 function renderOptions(s){
  const chain=s.optionChain,legs=Array.isArray(chain?.legs)?chain.legs:[];const valid=legs.filter(l=>['call','put'].includes(l.side)&&Number.isFinite(l.strike)&&l.strike>0&&typeof l.symbol==='string');
  const pairs=new Map();for(const leg of valid){if(!pairs.has(leg.strike))pairs.set(leg.strike,{strike:leg.strike});pairs.get(leg.strike)[leg.side]=leg;}optionPairs=[...pairs.values()].sort((a,b)=>b.strike-a.strike);
  setText('live-option-series',chain?.series||'尚未選擇組別');setText('live-option-state',`${chain?.status||'請在電腦工具選擇選擇權組別，再訂閱台指期。'}｜${valid.filter(l=>l.quoteReceipt>0).length}/${valid.length} 檔已收到報價。最多 9 個履約價；每格報價上方為買、下方為賣。資料不足留空。`);
  const body=document.getElementById('live-option-chain');body.replaceChildren();const reference=s.candles.length?s.candles.at(-1).close:(s.bid>0&&s.ask>0?(s.bid+s.ask)/2:null);const atm=reference===null||!optionPairs.length?null:optionPairs.reduce((a,b)=>Math.abs(a.strike-reference)<=Math.abs(b.strike-reference)?a:b).strike;
  function quoteCell(leg){const cell=document.createElement('td');if(!leg){cell.textContent='—';return cell;}if(leg.error){cell.textContent=leg.error;cell.className='muted';return cell;}if(!leg.quoteReceipt){cell.textContent='等待報價';cell.className='muted';return cell;}const bid=document.createElement('span'),ask=document.createElement('small');bid.textContent=leg.bid>0?leg.bid.toLocaleString('zh-TW'):'—';ask.textContent=leg.ask>0?leg.ask.toLocaleString('zh-TW'):'—';bid.style.display=ask.style.display='block';cell.append(bid,ask);cell.title=`${leg.symbol}：買 ${leg.bid}／賣 ${leg.ask}`;if(Date.now()-leg.quoteReceipt>60000){const stale=document.createElement('small');stale.style.display='block';stale.textContent='未更新';cell.append(stale);}return cell;}
  function tradeCell(leg){const cell=document.createElement('td');if(!leg||!leg.tradeReceipt){cell.textContent='—';return cell;}cell.textContent=leg.last.toLocaleString('zh-TW');const qty=document.createElement('small');qty.textContent=leg.lastQty+' 口';qty.style.display='block';cell.append(qty);return cell;}
  for(const pair of optionPairs){const row=document.createElement('tr');row.dataset.strike=pair.strike;row.className=pair.strike===atm?'atm':'';const strike=document.createElement('td');strike.className='strike';strike.textContent=pair.strike.toLocaleString('zh-TW');if(pair.strike===atm){const label=document.createElement('small');label.textContent='ATM';label.style.display='block';strike.append(label);}row.append(quoteCell(pair.call),tradeCell(pair.call),strike,tradeCell(pair.put),quoteCell(pair.put));body.append(row);}
  for(const side of ['call','put']){const list=valid.filter(l=>l.side===side);setText(side+'-total',list.some(l=>l.tradeReceipt>0)?list.reduce((sum,l)=>sum+(Number(l.volume)||0),0).toLocaleString('zh-TW'):'—');}setText('ratio',valid.length?`${valid.filter(l=>l.quoteReceipt>0).length}/${valid.length}`:'—');showOptionDetail();
  if(optionPairs.length&&!autoViewSet){autoViewSet=true;if(!userSelectedView)document.querySelector('[data-tab="chain"]').click();}
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
  candles=s.candles;drawCandles();renderOptions(s);window.RealFlow?.render(s);
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
   for(const t of incoming){if(s.connected&&Date.now()-t.receipt<=10000&&Date.now()-t.receipt>=0)window.receiveLiveTrade?.(t,t.symbol||s.symbol);}
  }
  lastSeq=Math.max(lastSeq,sequence);
 }catch(e){offline(e.message==='行情格式無效'?e.message:'無法連接本機行情工具');}finally{inFlight=false}}
 if(!loopback&&!window.remotePhoneMode){offline('真實行情模式需從 Windows 工具的「開啟看盤」進入，公開網站未接入行情');drawCandles();return;}
 window.refreshLiveDashboard=poll;poll();setInterval(poll,1000);
})();
