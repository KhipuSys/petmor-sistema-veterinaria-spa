const { chromium, request } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const base = process.env.QA_BASE_URL || 'http://127.0.0.1:5201';
if (!['127.0.0.1','localhost'].includes(new URL(base).hostname)) throw Error('Solo se admite un servidor local de pruebas.');
const out = path.resolve(__dirname, '../../Docs/QA/evidencias-correcciones');
fs.mkdirSync(out, {recursive:true});
const cases=[];
let cashierEmail='qa-cajero@petmor.com';
const disposableEmail='qa-borrar@petmor.com';
function record(id,title,pass,evidence) { const c={id,title,status:pass?'APROBADO':'FALLIDO',evidence}; cases.push(c); console.log(JSON.stringify(c)); }
async function token(api,url) { const r=await api.get(base+url); const h=await r.text(); const m=h.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/); if(!m) throw Error('Sin token: '+url); return m[1]; }
async function login(api,email,password) { const t=await token(api,'/Identity/Account/Login'); const r=await api.post(base+'/Identity/Account/Login',{form:{'Input.Email':email,'Input.Password':password,__RequestVerificationToken:t}}); if(new URL(r.url()).pathname.includes('Login')) throw Error('Login falló: '+email); }
async function form(api,url,fields,tokenUrl=url) { return api.post(base+url,{form:{...fields,__RequestVerificationToken:await token(api,tokenUrl)}}); }
async function json(api,url,data,t) { return api.post(base+url,{data,headers:t?{'RequestVerificationToken':t}:{}}); }
const blocked = r => r.status()===403 || (r.status()===302 && /Login|AccessDenied/.test(r.headers().location||''));

(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const anon=await request.newContext(); const admin=await request.newContext(); const cashier=await request.newContext();
 try {
  for(const url of ['/','/Productos','/Caja','/Usuarios','/Seguridad']) {
   const r=await anon.get(base+url,{maxRedirects:0}); record('AUTH-'+url,'Acceso anónimo bloqueado '+url,blocked(r),{http:r.status(),location:r.headers().location});
  }
  for(const url of ['/Clientes','/Categorias']) {
   const r=await anon.get(base+url,{maxRedirects:0}); record('ANON-'+url,'Datos privados exigen autenticación '+url,blocked(r),{http:r.status()});
  }
  const create=await anon.post(base+'/Clientes/Create',{form:{Documento:'QA000001',Nombre:'QA Cliente anonimo'},maxRedirects:0});
  record('ANON-WRITE','Un anónimo no puede crear clientes',blocked(create),{http:create.status()});
  await login(admin,'admin@petmor.com','Admin123*');
  const serverTime=await(await admin.get(base+'/Configuracion/HoraSistema')).json();
  const expectedPeru=new Date(Date.parse(serverTime.utc)-5*60*60*1000).toISOString().slice(0,19).replace('T',' ');
  record('TIME-PERU','Servidor usa hora Perú aunque su zona local sea distinta',Math.abs(Date.parse(serverTime.ahoraPeru.replace(' ','T')+'Z')-Date.parse(expectedPeru.replace(' ','T')+'Z'))<2000,{zone:serverTime.zona,offset:serverTime.desfase});
  await form(admin,'/Usuarios/Create',{Email:'qa-external@example.test',Password:'QaPrueba123*',Rol:'Cajero',Dni:'82000088',Nombres:'Otro',Apellidos:'Dominio'});
  record('EMAIL-CREATE-DOMAIN','Creación rechaza dominios distintos de petmor.com',!(await(await admin.get(base+'/Usuarios')).text()).includes('qa-external@example.test'),{});
  await form(admin,'/Clientes/Create',{Documento:'81000002',Nombre:'QA Cliente autorizado'});
  const listed=await (await admin.get(base+'/Clientes')).text();
  const clientId=listed.match(/href="\/Clientes\/Edit\/(\d+)"/)[1];
  const getMutation=await admin.get(base+'/Clientes/CambiarEstado/'+clientId,{maxRedirects:0});
  record('GET-MUTATION','Incluso autenticado, GET no cambia estado',getMutation.status()===405,{http:getMutation.status()});
  const clientToken=await token(admin,'/Clientes');
  await admin.post(base+'/Clientes/CambiarEstado/'+clientId,{form:{__RequestVerificationToken:clientToken}});
  const clientEdit=await(await admin.get(base+'/Clientes/Edit/'+clientId)).text();
  record('POST-MUTATION','Administrador puede cambiar estado por POST con token',!clientEdit.includes('checked="checked"'),{disabled:!clientEdit.includes('checked="checked"')});
  await form(admin,'/Usuarios/Create',{Email:cashierEmail,Password:'QaPrueba123*',Rol:'Cajero',Dni:'82000001',Nombres:'Ana',Apellidos:'Prueba'});
  const userList=await(await admin.get(base+'/Usuarios')).text();
  record('USER-PROFILE-CREATE','Usuario nuevo guarda DNI, nombres y apellidos',userList.includes('82000001') && userList.includes('Ana Prueba'),{});
  const prefixCreated=await form(admin,'/Usuarios/Create',{UsuarioAcceso:'qa-prefijo',Password:'QaPrueba123*',Rol:'Cajero',Dni:'82000011',Nombres:'Usuario',Apellidos:'Prefijo'});
  record('EMAIL-PREFIX-CREATE','Servidor completa dominio fijo al crear con solo usuario',new URL(prefixCreated.url()).pathname==='/Usuarios' && (await prefixCreated.text()).includes('qa-prefijo@petmor.com'),{});
  await form(admin,'/Usuarios/Create',{UsuarioAcceso:'intruso@example.test',Password:'QaPrueba123*',Rol:'Cajero',Dni:'82000012',Nombres:'Usuario',Apellidos:'Invalido'});
  record('EMAIL-PREFIX-INVALID','No se puede introducir otro dominio en el prefijo',!(await(await admin.get(base+'/Usuarios')).text()).includes('intruso@'),{});
  await form(admin,'/Usuarios/Create',{Email:'qa-duplicado@petmor.com',Password:'QaPrueba123*',Rol:'Cajero',Dni:'82000001',Nombres:'Otro',Apellidos:'Ejemplo'});
  record('USER-DNI-UNIQUE','DNI duplicado no crea una cuenta parcial',!(await(await admin.get(base+'/Usuarios')).text()).includes('qa-duplicado@petmor.com'),{});
  const invalidDni=await(await admin.get(base+'/Usuarios/ConsultarDni?dni=123')).json();
  record('DNI-VALIDATION','Consulta exige ocho dígitos antes de llamar al proveedor',invalidDni.exito===false,{});
  await login(cashier,cashierEmail,'QaPrueba123*');
  const dniDenied=await cashier.get(base+'/Usuarios/ConsultarDni?dni=82000001',{maxRedirects:0});
  record('DNI-ROLE','Consulta de datos de personal reservada al administrador',blocked(dniDenied),{http:dniDenied.status()});
  for(const url of ['/Usuarios','/Productos','/Configuracion','/Reportes/Ventas','/Seguridad']) {
   const r=await cashier.get(base+url,{maxRedirects:0}); record('ROLE-'+url,'Cajero no accede a administración '+url,blocked(r),{http:r.status(),location:r.headers().location});
  }
  for(const url of ['/Compras/Create','/Proveedores/Create']) {
   const r=await cashier.get(base+url,{maxRedirects:0}); record('ROLE-WRITE-'+url,'Funciones administrativas ocultas también restringidas en servidor '+url,blocked(r),{http:r.status()});
  }
  const cashierHome=await(await cashier.get(base+'/')).text();
  record('CASHIER-MENU','Cajero ve los tres nuevos apartados', ['/Ventas','/Clientes','/Caja/Historial'].every(url=>cashierHome.includes(`href="${url}"`)),{});
  for(const url of ['/Ventas','/Clientes','/Clientes/Create','/Caja/Historial']) {
   const response=await cashier.get(base+url,{maxRedirects:0});
   record('CASHIER-ACCESS-'+url,'Cajero accede a '+url,response.status()===200,{http:response.status()});
  }
  await form(cashier,'/Clientes/Create',{Documento:'81000003',Nombre:'Cliente creado por cajero'});
  const cashierClients=await(await cashier.get(base+'/Clientes')).text();
  record('CASHIER-CREATE-CLIENT','Cajero crea un cliente sin acciones administrativas',cashierClients.includes('Cliente creado por cajero') && !cashierClients.includes('href="/Clientes/Edit/') && !cashierClients.includes('action="/Clientes/CambiarEstado/'),{});
  const editClientUrl='/Clientes/Edit/'+clientId;
  const documentEdit=await form(admin,editClientUrl,{Id:clientId,Documento:'00123456',Nombre:'QA Cliente autorizado',Estado:true});
  let persistedClient=await(await admin.get(base+editClientUrl)).text();
  record('CLIENT-DOCUMENT-EDIT','Admin corrige DNI conservando ceros iniciales y estado inactivo',new URL(documentEdit.url()).pathname==='/Clientes' && persistedClient.includes('value="00123456"') && !(await(await admin.get(base+'/Ventas/BuscarClientes?term=00123456')).json()).results.length,{});
  const duplicateClient=await form(admin,editClientUrl,{Id:clientId,Documento:'81000003',Nombre:'QA Cliente autorizado'});
  persistedClient=await(await admin.get(base+editClientUrl)).text();
  record('CLIENT-DOCUMENT-DUPLICATE','Documento de otro cliente se rechaza sin sobrescribir', (await duplicateClient.text()).includes('Ya existe otro cliente') && persistedClient.includes('value="00123456"'),{});
  await form(admin,editClientUrl,{Id:clientId,Documento:'123',Nombre:'QA Cliente autorizado'});
  persistedClient=await(await admin.get(base+editClientUrl)).text();
  record('CLIENT-DOCUMENT-FORMAT','Documento inválido se rechaza en servidor',persistedClient.includes('value="00123456"'),{});
  await admin.post(base+'/Clientes/CambiarEstado/'+clientId,{form:{__RequestVerificationToken:await token(admin,'/Clientes')}});
  await form(admin,editClientUrl,{Id:clientId,Documento:'20123456789',Nombre:'QA Cliente autorizado'});
  const activeEdited=await(await admin.get(base+'/Ventas/BuscarClientes?term=20123456789')).json();
  record('CLIENT-EDIT-PRESERVES-ACTIVE','Editar RUC sin enviar Estado conserva cliente activo y mismo ID',activeEdited.results.some(c=>c.id===Number(clientId)),{});
  const forbiddenEdit=await cashier.get(base+'/Clientes/Edit/'+clientId,{maxRedirects:0});
  const forbiddenEditPost=await cashier.post(base+'/Clientes/Edit/'+clientId,{form:{Id:clientId,Documento:'QA000002',Nombre:'Intento no autorizado',__RequestVerificationToken:await token(cashier,'/Clientes')},maxRedirects:0});
  const forbiddenState=await cashier.post(base+'/Clientes/CambiarEstado/'+clientId,{form:{__RequestVerificationToken:await token(cashier,'/Clientes')},maxRedirects:0});
  record('CASHIER-CLIENT-LIMITS','Edición y cambio de estado de clientes siguen reservados a administrador',blocked(forbiddenEdit) && blocked(forbiddenEditPost) && blocked(forbiddenState),{edit:forbiddenEdit.status(),editPost:forbiddenEditPost.status(),state:forbiddenState.status()});
  let r=await json(admin,'/Ventas/Registrar',{}); record('CSRF-SALE','Registrar venta exige token antifalsificación',r.status()===400,{http:r.status()});
  r=await admin.post(base+'/Caja/Abrir',{form:{montoInicial:10}}); record('CSRF-BOX','Abrir caja exige token antifalsificación',r.status()===400,{http:r.status()});
  const usersToken=await token(admin,'/Usuarios/Create');
  r=await json(admin,'/Ventas/Registrar',{OperacionId:crypto.randomUUID(),MetodoPago:'Efectivo',Total:3.5,Detalles:[{ProductoId:1,Cantidad:1}]},usersToken);
  const closed=await r.json(); record('CLOSED-BOX','Caja cerrada impide vender',closed.exito===false && closed.mensaje.includes('abrir caja'),closed);
  r=await form(admin,'/Caja/Abrir',{montoInicial:-1},'/Caja'); record('NEGATIVE-BOX','Apertura negativa rechazada',r.status()===400,{http:r.status()});
  await form(admin,'/Caja/Abrir',{montoInicial:100},'/Caja');
  await admin.post(base+'/Caja/Abrir',{form:{montoInicial:999,__RequestVerificationToken:usersToken}});
  const box=await(await admin.get(base+'/Caja')).text(); record('DUPLICATE-BOX','Segunda apertura conserva caja original',box.includes('100.00') && !box.includes('999.00'),{originalAmountPreserved:box.includes('100.00')});
  const saleToken=await token(admin,'/Ventas/Create');
  const product=(await(await admin.get(base+'/Ventas/BuscarProductos?term=Coca')).json()).results[0];
  const sale={OperacionId:crypto.randomUUID(),MetodoPago:'Efectivo',Total:product.precio,Descuento:0,Detalles:[{ProductoId:product.id,Cantidad:1,Precio:product.precio,Total:product.precio}]};
  r=await json(admin,'/Ventas/Registrar',sale,saleToken); const s1=await r.json();
  r=await json(admin,'/Ventas/Registrar',sale,saleToken); const s2=await r.json();
  record('E2E-SALE','Venta HTTP real y reintento sin duplicación',s1.exito && s2.exito && s1.idVenta===s2.idVenta && s2.repetida,{first:s1,second:s2});
  const ticket=await admin.get(base+'/Ventas/Ticket/'+s1.idVenta); record('TICKET','Ticket de venta responde',ticket.status()===200,{http:ticket.status()});
  const legacyTicket=await ticket.text();
  record('TICKET-LEGACY-NAME','Usuario antiguo sin perfil no expone correo en ticket',legacyTicket.includes('Nombre pendiente de registrar') && !legacyTicket.includes('admin@petmor.com'),{});
  r=await cashier.get(base+'/Ventas/Ticket/'+s1.idVenta,{maxRedirects:0}); record('SALE-PRIVACY','Cajero no consulta ticket de otro vendedor',blocked(r),{http:r.status(),ventaDeOtroUsuario:s1.idVenta});
  r=await cashier.post(base+'/Ventas/Anular',{form:{id:s1.idVenta,motivo:'QA',__RequestVerificationToken:await token(cashier,'/Caja')},maxRedirects:0}); record('VOID-ROLE','Solo administrador puede anular',blocked(r),{http:r.status()});
  r=await admin.post(base+'/Ventas/Anular',{form:{id:s1.idVenta,motivo:'QA anulación',__RequestVerificationToken:saleToken}}); const cancel=await r.json();
  const afterProduct=(await(await admin.get(base+'/Ventas/BuscarProductos?term=Coca')).json()).results[0];
  record('E2E-VOID','Anulación repone existencias',cancel.exito && afterProduct.stock===product.stock,{before:product.stock,after:afterProduct.stock});
  await form(admin,'/Caja/Cerrar',{montoCierre:100},'/Caja');
  r=await admin.get(base+'/Ventas/Create',{maxRedirects:0}); record('E2E-CLOSE','Tras cerrar caja el POS redirige a caja',r.status()===302 && r.headers().location==='/Caja',{http:r.status(),location:r.headers().location});
  await form(cashier,'/Caja/Abrir',{montoInicial:20},'/Caja');
  const cashierSale={...sale,OperacionId:crypto.randomUUID()};
  const cashierSaleToken=await token(cashier,'/Ventas/Create');
  const cashierHighDiscount={...sale,OperacionId:crypto.randomUUID(),Total:Number((product.precio*0.6).toFixed(2)),Descuento:Number((product.precio*0.4).toFixed(2))};
  const highDiscountResult=await(await json(cashier,'/Ventas/Registrar',cashierHighDiscount,cashierSaleToken)).json();
  record('DISCOUNT-CASHIER-LIMIT','Cajero no puede superar 30% de descuento',highDiscountResult.exito===false && highDiscountResult.mensaje.includes('30%'),highDiscountResult);
  const ownSale=await(await json(cashier,'/Ventas/Registrar',cashierSale,cashierSaleToken)).json();
  const ownHistory=await(await cashier.get(base+'/Ventas')).text();
  const ownTicket=await cashier.get(base+'/Ventas/Ticket/'+ownSale.idVenta,{maxRedirects:0});
  const ownTicketHtml=await ownTicket.text();
  record('TICKET-PERSONAL-NAME','Ticket muestra nombre y apellido, no correo del cajero',ownTicketHtml.includes('Ana Prueba') && !ownTicketHtml.includes(cashierEmail),{});
  record('CASHIER-SALES-HISTORY','Cajero ve su venta y ticket sin botón de anulación',ownSale.exito && ownTicket.status()===200 && ownHistory.includes(`href="/Ventas/Ticket/${ownSale.idVenta}"`) && !ownHistory.includes(`href="/Ventas/Ticket/${s1.idVenta}"`) && !/class="[^"]*btn-anular/.test(ownHistory),{venta:ownSale.idVenta,ticket:ownTicket.status()});
  const ownVoid=await cashier.post(base+'/Ventas/Anular',{form:{id:ownSale.idVenta,motivo:'Intento QA',__RequestVerificationToken:cashierSaleToken},maxRedirects:0});
  record('CASHIER-OWN-VOID-DENIED','Cajero tampoco puede anular su propia venta por URL',blocked(ownVoid),{http:ownVoid.status()});
  await form(cashier,'/Caja/Cerrar',{montoCierre:20+product.precio},'/Caja');
  const ownBoxes=await(await cashier.get(base+'/Caja/Historial')).text();
  const allBoxes=await(await admin.get(base+'/Caja/Historial')).text();
  record('CASHIER-BOX-ISOLATION','Cajero ve sus cierres; administrador ve ambos usuarios',ownBoxes.includes(cashierEmail) && !ownBoxes.includes('admin@petmor.com') && allBoxes.includes(cashierEmail) && allBoxes.includes('admin@petmor.com'),{});
  const adminSales=await(await admin.get(base+'/Ventas')).text();
  record('ADMIN-VOID-BUTTON','Administrador conserva el botón para anular ventas activas',/class="[^"]*btn-anular/.test(adminSales),{});
  const cashierUserRow=userList.split('<tr>').find(row=>row.includes(cashierEmail));
  const cashierId=cashierUserRow.match(/href="\/Usuarios\/Edit\/([^"]+)"/)[1];
  const editUrl='/Usuarios/Edit/'+cashierId;
  await form(admin,'/Usuarios/Edit',{Id:cashierId,Email:cashierEmail,Dni:'82000001',Nombres:'Ana Maria',Apellidos:'Prueba',NuevoRol:'Cajero'},editUrl);
  const updatedTicket=await(await cashier.get(base+'/Ventas/Ticket/'+ownSale.idVenta)).text();
  record('USER-PROFILE-EDIT','Editar nombres conserva un nombre y un apellido en ticket y el acceso de cajero',updatedTicket.includes('Ana Prueba') && !updatedTicket.includes('Ana Maria Prueba'),{});
  await form(admin,'/Usuarios/Edit',{Id:cashierId,Email:cashierEmail,Dni:'82000001',Nombres:'No guardar',Apellidos:'Prueba',NuevoRol:'Administrador',NuevaPassword:'x'},editUrl);
  const afterInvalidEdit=await(await admin.get(base+editUrl)).text();
  record('USER-EDIT-ATOMIC','Edición inválida no cambia nombre ni rol',afterInvalidEdit.includes('value="Ana Maria"') && !afterInvalidEdit.includes('value="No guardar"') && /selected="selected" value="Cajero"|value="Cajero" selected="selected"/.test(afterInvalidEdit),{});
  const deletionToken=await token(admin,'/Usuarios');
  const historicalDeletion=await admin.post(base+'/Usuarios/Delete',{form:{id:cashierId,__RequestVerificationToken:deletionToken}});
  const historicalResult=await historicalDeletion.json();
  record('USER-DELETE-HISTORY','Cuenta con ventas/caja devuelve motivo y conserva usuario e historial',historicalResult.exito===false && historicalResult.codigo==='USUARIO_CON_HISTORIAL' && (await(await admin.get(base+'/Usuarios')).text()).includes(cashierEmail) && (await admin.get(base+'/Ventas/Ticket/'+ownSale.idVenta)).status()===200,{http:historicalDeletion.status(),response:historicalResult});
  const adminRow=userList.split('<tr>').find(row=>row.includes('admin@petmor.com') && /href="\/Usuarios\/Edit\//.test(row));
  const adminId=adminRow.match(/href="\/Usuarios\/Edit\/([^"]+)"/)[1];
  const selfDelete=await(await admin.post(base+'/Usuarios/Delete',{form:{id:adminId,__RequestVerificationToken:deletionToken}})).json();
  record('USER-DELETE-SELF','Administrador no se elimina a sí mismo',selfDelete.exito===false,{});
  const cashierDelete=await cashier.post(base+'/Usuarios/Delete',{form:{id:adminId,__RequestVerificationToken:await token(cashier,'/Caja')},maxRedirects:0});
  record('USER-DELETE-ROLE','Cajero no puede eliminar cuentas',blocked(cashierDelete),{http:cashierDelete.status()});

  await form(admin,'/Proveedores/Create',{Ruc:'20123456789',RazonSocial:'QA Proveedor',Estado:true});
  const supplier=(await(await admin.get(base+'/Proveedores/BuscarProveedores?term=QA')).json()).results[0];
  const purchase={ProveedorId:supplier.id,NumeroDocumento:'QA-TAMPER',Total:0.01,Detalles:[{ProductoId:product.id,Cantidad:2,Precio:5,Total:0.01}]};
  r=await cashier.post(base+'/Compras/Registrar',{data:purchase,maxRedirects:0});
  record('PURCHASE-ROLE','Cajero no registra compras por URL',blocked(r),{http:r.status()});
  r=await json(admin,'/Compras/Registrar',purchase);
  record('PURCHASE-CSRF','Compra requiere token antifalsificación',r.status()===400,{http:r.status()});
  const purchaseToken=await token(admin,'/Compras/Create');
  r=await json(admin,'/Compras/Registrar',purchase,purchaseToken); const buy=await r.json();
  record('PURCHASE-TOTAL','Compra rechaza total incoherente (2 × 5 frente a 0.01)',buy.exito===false,{response:buy,submittedTotal:0.01,expectedTotal:10});
  purchase.Total=10; purchase.Detalles[0].Total=10;
  r=await json(admin,'/Compras/Registrar',purchase,purchaseToken); const correctBuy=await r.json();
  record('PURCHASE-VALID','Administrador registra compra válida con token',correctBuy.exito===true,{response:correctBuy});

  // Catálogo real y paginación: no basta con probar una búsqueda HTTP con texto.
  for(let i=0;i<21;i++) await form(admin,'/Productos/Create',{Nombre:'QA Catalogo '+String(i).padStart(2,'0'),CodigoBarras:'QACAT'+i,Precio:2,Costo:1,Stock:i===0?0:5,StockMinimo:1,CategoriaId:1,Estado:true});
  const catalog1=await(await admin.get(base+'/Ventas/BuscarProductos')).json();
  const catalog2=await(await admin.get(base+'/Ventas/BuscarProductos?page=2')).json();
  record('CATALOG-PAGING','Catálogo sin escribir y segunda página sin duplicados',catalog1.results.length===20 && catalog1.pagination.more && catalog2.results.length>0 && !catalog2.results.some(p=>catalog1.results.some(q=>q.id===p.id)),{first:catalog1.results.length,second:catalog2.results.length});
  const zeroProduct=(await(await admin.get(base+'/Ventas/BuscarProductos?term=QACAT0')).json()).results[0];
  record('CATALOG-NOSTOCK','Producto sin existencias se muestra deshabilitado para vender',zeroProduct.stock===0 && zeroProduct.disabled===true,{});
  const deactivated=(await(await admin.get(base+'/Ventas/BuscarProductos?term=QACAT20')).json()).results[0];
  await admin.post(base+'/Productos/CambiarEstado/'+deactivated.id,{form:{__RequestVerificationToken:await token(admin,'/Productos')}});
  const inactiveResults=await(await admin.get(base+'/Ventas/BuscarProductos?term=QACAT20')).json();
  record('CATALOG-INACTIVE','Productos inactivos no aparecen en POS',inactiveResults.results.length===0,{});
  for(const [role,api] of [['admin',admin],['cajero',cashier]]) {
   await form(api,'/Caja/Abrir',{montoInicial:50},'/Caja');
   const uiContext=await browser.newContext({storageState:await api.storageState()});
   const ui=await uiContext.newPage(); const uiErrors=[]; ui.on('pageerror',e=>uiErrors.push(e.message));
   await ui.goto(base+'/Ventas/Create');
   await ui.locator('#cboProducto + .select2 .select2-selection').click();
   await ui.locator('.select2-results__option--selectable').first().waitFor();
   record('POS-OPEN-'+role,'Selector muestra productos sin escribir: '+role,await ui.locator('.select2-results__option--selectable').count()>0,{});
   await ui.locator('.select2-search__field').fill(product.codigo || '77501');
   await ui.locator('.select2-results__option--selectable').filter({hasText:'Coca Cola'}).first().click();
   await ui.locator('#txtCantidad').fill('1.5'); await ui.locator('#btnAgregar').click();
   await ui.locator('.swal2-popup').waitFor();
   record('POS-FRACTION-'+role,'Cantidad fraccionaria rechazada en interfaz',await ui.locator('#tablaDetalle tr').count()===0,{});
   await ui.locator('.swal2-confirm').click();
   await ui.locator('#txtCantidad').fill('2'); await ui.locator('#btnAgregar').click();
   await ui.locator('#txtMontoRecibido').fill('20');
   await ui.locator('#txtDescuento').fill('1');
   await ui.locator('#txtDescuento').dispatchEvent('input');
   const expectedChange=(20-(product.precio*2-1)).toFixed(2);
   record('POS-CHANGE-'+role,'Vuelto se recalcula al cambiar descuento',await ui.locator('#txtVuelto').inputValue()===expectedChange,{expected:expectedChange,actual:await ui.locator('#txtVuelto').inputValue()});
   await ui.locator('#cboCliente + .select2 .select2-selection').click();
   await ui.locator('.select2-results__option--selectable').filter({hasText:'Cliente creado por cajero'}).click();
   await ui.locator('#btnTerminar').click();
   const responsePromise=ui.waitForResponse(r=>r.url().endsWith('/Ventas/Registrar') && r.request().method()==='POST');
   await ui.locator('.swal2-confirm').click();
   const registered=await(await responsePromise).json();
   await ui.getByText('¡Venta Exitosa!',{exact:true}).waitFor();
   record('POS-SALE-'+role,'Venta completa desde la interfaz con cliente y descuento',registered.exito && await ui.locator('#tablaDetalle tr').count()===0 && uiErrors.length===0,{id:registered.idVenta,errors:uiErrors});
   await ui.locator('.swal2-cancel').click();
   const currentStock=(await(await api.get(base+'/Ventas/BuscarProductos?term=77501')).json()).results[0];
   record('POS-REFRESH-'+role,'Nueva búsqueda refleja stock después de la venta',currentStock.stock===product.stock+2-(role==='admin'?3:5),{stock:currentStock.stock});
   await uiContext.close();
   await form(api,'/Caja/Cerrar',{montoCierre:50+product.precio*2-1},'/Caja');
  }

  const buyUiContext=await browser.newContext({storageState:await admin.storageState()});
  const buyUi=await buyUiContext.newPage(); await buyUi.goto(base+'/Compras/Create');
  await buyUi.locator('#cboProveedor + .select2 .select2-selection').click();
  await buyUi.locator('.select2-results__option--selectable').filter({hasText:'QA Proveedor'}).click();
  await buyUi.locator('#cboProducto + .select2 .select2-selection').click();
  await buyUi.locator('.select2-results__option--selectable').first().waitFor();
  await buyUi.locator('.select2-search__field').fill('77501');
  await buyUi.locator('.select2-results__option--selectable').filter({hasText:'Coca Cola'}).click();
  record('BUY-COST','Compras propone costo de compra, no precio de venta',await buyUi.locator('#txtPrecioCompra').inputValue()==='5.00',{});
  await buyUi.locator('#btnAgregar').click(); await buyUi.locator('#btnTerminar').click();
  const buyResponse=buyUi.waitForResponse(r=>r.url().endsWith('/Compras/Registrar') && r.request().method()==='POST');
  await buyUi.locator('.swal2-confirm').click();
  const bought=await(await buyResponse).json();
  record('BUY-UI','Compra completa desde selectores visibles sin búsqueda inicial',bought.exito,{response:bought});
  await buyUiContext.close();

  const ctx=await browser.newContext({storageState:await admin.storageState()}); const page=await ctx.newPage();
  await page.goto(base+'/Usuarios');
  await page.locator('tr').filter({hasText:cashierEmail}).locator('.btn-eliminar').click();
  await page.locator('.swal2-confirm').click();
  await page.getByText('No se puede eliminar',{exact:true}).waitFor();
  record('USER-DELETE-UI-HISTORY','Pantalla muestra historial asociado y no falso error de conexión',(await page.locator('.swal2-html-container').innerText()).includes('ventas'),{});
  await page.locator('.swal2-confirm').click();
  await form(admin,'/Usuarios/Create',{Email:disposableEmail,Password:'QaPrueba123*',Rol:'Cajero',Dni:'82000009',Nombres:'Usuario',Apellidos:'Borrable'});
  await page.reload();
  await page.locator('tr').filter({hasText:disposableEmail}).locator('.btn-eliminar').click();
  await page.locator('.swal2-confirm').click();
  await page.getByText('¡Eliminado!',{exact:true}).waitFor();
  record('USER-DELETE-UI-EMPTY','Cuenta sin historial se elimina desde el botón',!(await(await admin.get(base+'/Usuarios')).text()).includes(disposableEmail),{});
  await page.locator('.swal2-confirm').click();
  await page.waitForLoadState('networkidle');
  const reuse=await form(admin,'/Usuarios/Create',{Email:disposableEmail,Password:'QaPrueba123*',Rol:'Cajero',Dni:'82000009',Nombres:'Usuario',Apellidos:'Recreado'});
  record('USER-DELETE-PROFILE','Eliminar cuenta también elimina perfil permitiendo reutilizar DNI',new URL(reuse.url()).pathname==='/Usuarios' && (await reuse.text()).includes('Usuario Recreado'),{});
  await page.goto(base+editClientUrl);
  record('CLIENT-DNI-EDITABLE-UI','Campo de documento editable para admin',await page.locator('#Documento').isEditable(),{});
  await page.route('**/Clientes/ConsultarDocumento?*',route=>route.fulfill({json:{exito:true,nombre:'PERSONA CONSULTADA QA',direccion:'Direccion QA'}}));
  await page.locator('#Documento').fill('00123457'); await page.locator('#btnConsultarDocumento').click();
  await page.waitForFunction(()=>document.querySelector('#Nombre').value==='PERSONA CONSULTADA QA');
  record('CLIENT-LOOKUP-DNI','Consulta DNI desde edición completa nombre y dirección',await page.locator('#Direccion').inputValue()==='Direccion QA',{});
  record('CLIENT-LOOKUP-NOSAVE','Consultar no guarda cambios automáticamente',(await(await admin.get(base+editClientUrl)).text()).includes('value="20123456789"'),{});
  await page.unroute('**/Clientes/ConsultarDocumento?*');
  await page.route('**/Clientes/ConsultarDocumento?*',route=>route.fulfill({json:{exito:true,nombre:'EMPRESA QA SAC',direccion:'Direccion empresa QA'}}));
  await page.locator('#Documento').fill('20987654321'); await page.locator('#btnConsultarDocumento').click();
  await page.waitForFunction(()=>document.querySelector('#Nombre').value==='EMPRESA QA SAC');
  record('CLIENT-LOOKUP-RUC','Consulta RUC completa razón social y dirección',await page.locator('#Direccion').inputValue()==='Direccion empresa QA',{});
  await page.unroute('**/Clientes/ConsultarDocumento?*');
  await page.route('**/Clientes/ConsultarDocumento?*',route=>route.fulfill({json:{exito:false,mensaje:'Sin datos QA'}}));
  await page.locator('#btnConsultarDocumento').click(); await page.getByText('Sin datos QA',{exact:true}).waitFor();
  record('CLIENT-LOOKUP-ERROR','Consulta fallida conserva los datos ingresados',await page.locator('#Nombre').inputValue()==='EMPRESA QA SAC' && await page.locator('#Direccion').inputValue()==='Direccion empresa QA',{});
  await page.unroute('**/Clientes/ConsultarDocumento?*');
  await page.route('**/Clientes/ConsultarDocumento?*',async route=>{await new Promise(r=>setTimeout(r,600));await route.fulfill({json:{exito:true,nombre:'RESPUESTA ANTIGUA'}});});
  await page.locator('#btnConsultarDocumento').click(); await page.locator('#Documento').fill('00123457');
  await page.getByText('El documento cambió durante la consulta. Pulsa Consultar nuevamente.',{exact:true}).waitFor();
  record('CLIENT-LOOKUP-STALE','Respuesta de un documento anterior no sobrescribe el formulario',await page.locator('#Nombre').inputValue()==='EMPRESA QA SAC',{});
  await page.unroute('**/Clientes/ConsultarDocumento?*');
  await page.locator('#Documento').fill('00123457');
  await Promise.all([page.waitForURL(u=>u.pathname==='/Clientes'),page.locator('form[action*="Edit"] button[type="submit"]').click()]);
  record('CLIENT-DNI-UI-SAVE','Corrección de DNI guardada desde el formulario',(await(await admin.get(base+editClientUrl)).text()).includes('value="00123457"'),{});
  await page.goto(base+'/Usuarios/Create');
  record('EMAIL-CREATE-FIXED-DOMAIN','Formulario Crear muestra dominio fijo sin el mensaje anterior',await page.locator('#dominioCorreo').innerText()==='@petmor.com' && await page.locator('input[name="Email"]').count()===0 && !(await page.locator('body').innerText()).includes('Solo se permite @petmor.com'),{});
  await page.route('**/Usuarios/ConsultarDni?*',route=>route.fulfill({json:{exito:true,nombres:'LUIS ALBERTO',apellidos:'PRUEBA DEMO'}}));
  await page.locator('#Dni').fill('82000002'); await page.locator('[data-buscar-dni]').click();
  await page.waitForFunction(()=>document.querySelector('#Nombres').value==='LUIS ALBERTO');
  record('DNI-UI-FILL','Buscar DNI completa nombres y apellidos en formulario',await page.locator('#Apellidos').inputValue()==='PRUEBA DEMO',{});
  await page.unroute('**/Usuarios/ConsultarDni?*');
  await page.route('**/Usuarios/ConsultarDni?*',route=>route.fulfill({json:{exito:false,mensaje:'Proveedor no disponible'}}));
  await page.locator('[data-buscar-dni]').click();
  await page.getByText('Proveedor no disponible',{exact:true}).waitFor();
  record('DNI-UI-FAILURE','Error de proveedor conserva datos y permite edición manual',await page.locator('#Nombres').inputValue()==='LUIS ALBERTO' && await page.locator('#Nombres').isEditable(),{});
  await page.unroute('**/Usuarios/ConsultarDni?*');
  await page.locator('#UsuarioAcceso').fill('qa-creado-ui');
  await page.locator('#Password').fill('QaPrueba123*');
  await page.locator('#Rol').selectOption('Cajero');
  await Promise.all([page.waitForURL(u=>u.pathname==='/Usuarios'),page.locator('form[action*="Create"] button[type="submit"]').click()]);
  record('EMAIL-PREFIX-CREATE-UI','Crear usuario desde navegador completa el correo',(await page.locator('body').innerText()).includes('qa-creado-ui@petmor.com'),{});
  const jsErrors=[]; page.on('pageerror',e=>jsErrors.push(e.message));
  for(const width of [320,390,768,1440]) {
   await page.setViewportSize({width,height:844});
   for(const url of ['/','/Productos','/Caja','/Ventas','/Reportes/Ventas']) {
    await page.goto(base+url); await page.waitForTimeout(450);
    const sizes=await page.evaluate(()=>{const e=document.querySelector('.main-content');return {width:e.clientWidth,scroll:e.scrollWidth};});
    record(`LAYOUT-${width}-${url}`,'Contenido sin desbordamiento '+url+' a '+width,sizes.scroll<=sizes.width+2,sizes);
   }
   if(width===390) await page.screenshot({path:path.join(out,'reporte-movil.png'),fullPage:true});
  }
  await page.setViewportSize({width:390,height:844}); await page.goto(base+'/');
  await page.getByRole('button',{name:'Abrir menú principal'}).click();
  await page.locator('#sidebar-wrapper.show').waitFor(); await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
  record('MENU-KEYBOARD','Menú móvil abre y cierra con Escape',!await page.locator('#sidebar-wrapper').evaluate(e=>e.classList.contains('show')),{});
  for(const url of ['/','/Productos','/Caja','/Reportes/Ventas']) {
   await page.goto(base+url); await page.addScriptTag({path:require.resolve('axe-core/axe.min.js')});
   const audit=await page.evaluate(()=>axe.run(document,{runOnly:{type:'tag',values:['wcag2a','wcag2aa','wcag21aa']}}));
   const violations=audit.violations.map(v=>({id:v.id,impact:v.impact,description:v.description,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary})),helpUrl:v.helpUrl}));
   fs.writeFileSync(path.join(out,'axe-'+(url.replaceAll('/','-')||'home')+'.json'),JSON.stringify(violations,null,2));
   record('A11Y-'+url,'Revisión automática WCAG A/AA '+url,violations.length===0,{violations:violations.map(v=>({id:v.id,impact:v.impact,elements:v.nodes.length}))});
  }
  record('JS','Sin errores JavaScript en pantallas recorridas',jsErrors.length===0,{errors:[...new Set(jsErrors)]});
  // Marcador local inocuo: demuestra ejecución de HTML sin extraer datos ni enviar solicitudes.
  await page.goto(base+'/');
  await page.route('**/IA/Consultar',route=>route.fulfill({json:{respuesta:'Respuesta simulada QA'}}));
  await page.locator('#ai-toggle-btn').click();
  await page.locator('#ai-user-input').fill('<img src=x onerror="window.qaHtmlExecuted=true">');
  await page.locator('#ai-send-btn').click(); await page.waitForTimeout(300);
  record('CHAT-XSS','Chat trata la entrada como texto',!await page.evaluate(()=>window.qaHtmlExecuted===true),{htmlHandlerExecuted:await page.evaluate(()=>window.qaHtmlExecuted===true)});

  const times=[]; const statuses=[];
  await admin.get(base+'/Productos');
  for(let batch=0;batch<4;batch++) await Promise.all(Array.from({length:5},async()=>{const start=performance.now(); const response=await admin.get(base+'/Productos'); times.push(performance.now()-start); statuses.push(response.status());}));
  times.sort((a,b)=>a-b);
  record('PERF-SMOKE','Carga ligera local: 20 lecturas, concurrencia 5, sin errores',statuses.every(s=>s===200),{requests:20,concurrency:5,p50ms:Math.round(times[9]),p95ms:Math.round(times[18]),maxMs:Math.round(times[19]),statuses});

  const profileFields={Id:cashierId,Dni:'82000001',Nombres:'Ana Maria',Apellidos:'Prueba',NuevoRol:'Cajero'};
  await form(admin,'/Usuarios/Edit',{...profileFields,Email:'qa-externo@example.test'},editUrl);
  record('EMAIL-EDIT-DOMAIN','Edición rechaza dominios ajenos',(await(await admin.get(base+editUrl)).text()).includes(`value="${cashierEmail.split('@')[0]}"`),{});
  await form(admin,'/Usuarios/Edit',{...profileFields,Email:'admin@petmor.com'},editUrl);
  record('EMAIL-EDIT-DUPLICATE','Edición rechaza correo de otra cuenta',(await(await admin.get(base+editUrl)).text()).includes(`value="${cashierEmail.split('@')[0]}"`),{});
  // Mantener una caja abierta durante el cambio para verificar que sigue perteneciendo al usuario.
  await form(cashier,'/Caja/Abrir',{montoInicial:77},'/Caja');
  await page.goto(base+editUrl);
  record('EMAIL-EDITABLE','Administrador escribe solo usuario y ve dominio fijo en Editar',await page.locator('#UsuarioAcceso').isEditable() && await page.locator('#dominioCorreo').innerText()==='@petmor.com' && await page.locator('input[name="Email"]').count()===0,{});
  const oldCashierEmail=cashierEmail;
  cashierEmail='qa-cajero-nuevo@petmor.com';
  await page.locator('#UsuarioAcceso').fill(cashierEmail.split('@')[0]);
  await Promise.all([page.waitForURL(u=>u.pathname==='/Usuarios'),page.locator('form[action*="Edit"] button[type="submit"]').click()]);
  const oldSession=await cashier.get(base+'/Caja',{maxRedirects:0});
  record('EMAIL-OLD-SESSION','Cambio de correo invalida sesión anterior',blocked(oldSession),{http:oldSession.status()});
  const oldLogin=await form(cashier,'/Identity/Account/Login',{'Input.Email':oldCashierEmail,'Input.Password':'QaPrueba123*'});
  record('EMAIL-OLD-LOGIN','Correo anterior ya no permite iniciar sesión',new URL(oldLogin.url()).pathname.includes('Login'),{});
  await login(cashier,cashierEmail,'QaPrueba123*');
  const preservedBox=await(await cashier.get(base+'/Caja')).text();
  const preservedHistory=await(await cashier.get(base+'/Caja/Historial')).text();
  const preservedSales=await(await cashier.get(base+'/Ventas')).text();
  record('EMAIL-HISTORY','Correo nuevo conserva caja abierta, cierres y ventas',preservedBox.includes('77.00') && preservedHistory.includes(cashierEmail) && preservedSales.includes(`href="/Ventas/Ticket/${ownSale.idVenta}"`),{});

  const wrong=await request.newContext(); let last;
  for(let i=0;i<6;i++) last=await form(wrong,'/Identity/Account/Login',{'Input.Email':cashierEmail,'Input.Password':'Incorrecta-QA'});
  const loginAfterFailures=await form(wrong,'/Identity/Account/Login',{'Input.Email':cashierEmail,'Input.Password':'QaPrueba123*'});
  const acceptedAfterFailures=!new URL(loginAfterFailures.url()).pathname.includes('Login');
  record('LOGIN-LOCKOUT','Intentos fallidos repetidos activan bloqueo de cuenta',!acceptedAfterFailures,{failedAttempts:6,correctPasswordStillAccepted:acceptedAfterFailures});
  const adminEdit='/Usuarios/Edit/'+adminId;
  const selfEdit=await form(admin,'/Usuarios/Edit',{Id:adminId,Email:'administrador-nuevo@petmor.com',Dni:'82000099',Nombres:'Admin',Apellidos:'Prueba',NuevoRol:'Administrador'},adminEdit);
  const selfCheck=await admin.get(base+'/Usuarios',{maxRedirects:0});
  record('EMAIL-SELF-RENAME','Administrador cambia su propio correo y conserva sesión actual',new URL(selfEdit.url()).pathname==='/Usuarios' && selfCheck.status()===200 && (await selfCheck.text()).includes('administrador-nuevo@petmor.com'),{});
  await wrong.dispose(); await ctx.close();
 } finally {
  fs.writeFileSync(path.join(out,'resultados.json'),JSON.stringify({date:new Date().toISOString(),base,cases,summary:{total:cases.length,passed:cases.filter(c=>c.status==='APROBADO').length,failed:cases.filter(c=>c.status==='FALLIDO').length}},null,2));
  await browser.close(); await anon.dispose(); await admin.dispose(); await cashier.dispose();
  if(cases.some(c=>c.status==='FALLIDO')) process.exitCode=1;
 }
})().catch(e=>{console.error(e);process.exitCode=1;});
