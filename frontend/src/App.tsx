import { FormEvent, ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { readAuth, writeAuth } from './apiClient';
import { authApi, orderApi, productApi, reportApi } from './api';
import type { AuthResponse, CustomerReport, Order, PagedResponse, Product, ProductFilters, UserRole } from './models';

const money = (value: number) => new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value);
const CART_KEY = 'oi-cart';
const PRODUCT_CACHE_KEY = 'oi-product-cache';
const defaultFilters: ProductFilters = { search: '', minPrice: '', maxPrice: '', inStock: '', pageNumber: 1, pageSize: 9, sortBy: 'name', sortDirection: 'asc' };
function filtersFromUrl(): ProductFilters {
    const q = new URLSearchParams(window.location.search);
    return { ...defaultFilters, search: q.get('search') || '', minPrice: q.get('minPrice') || '', maxPrice: q.get('maxPrice') || '', inStock: q.get('inStock') || '', pageNumber: Math.max(1, Number(q.get('pageNumber') || 1)), pageSize: Math.max(1, Number(q.get('pageSize') || 9)), sortBy: q.get('sortBy') || 'name', sortDirection: q.get('sortDirection') || 'asc' };
}
function navigate(path: string) { window.history.pushState({}, '', path); window.dispatchEvent(new PopStateEvent('popstate')); }
function readCart(): Record<number, number> { try { return JSON.parse(localStorage.getItem(CART_KEY) || '{}') as Record<number, number>; } catch { return {}; } }
function readProductCache(): Record<number, Product> { try { return JSON.parse(localStorage.getItem(PRODUCT_CACHE_KEY) || '{}') as Record<number, Product>; } catch { return {}; } }
function getError(error: unknown): string { return error instanceof Error ? error.message : 'Something went wrong. Please try again.'; }

function Loading({ label = 'Loading…' }: { label?: string }) { return <div className="state loading" role="status"><span className="spinner" />{label}</div>; }
function Empty({ title, detail }: { title: string; detail: string }) { return <div className="state empty"><strong>{title}</strong><p>{detail}</p></div>; }
function ErrorState({ message, retry }: { message: string; retry?: () => void }) { return <div className="state error" role="alert"><strong>Could not load this content</strong><p>{message}</p>{retry && <button className="button secondary" onClick={retry}>Try again</button>}</div>; }
function Guard({ role, auth, children }: { role?: UserRole; auth: AuthResponse | null; children: ReactNode }) {
    if (!auth) return <div className="state empty"><strong>Sign in required</strong><p>Please sign in to continue.</p><button className="button" onClick={() => navigate('/login')}>Go to sign in</button></div>;
    if (role && auth.role !== role) return <div className="state error"><strong>Access denied</strong><p>This page is not available for your account role.</p><button className="button secondary" onClick={() => navigate('/catalog')}>Back to catalog</button></div>;
    return <>{children}</>;
}

export default function App() {
    const [auth, setAuth] = useState<AuthResponse | null>(() => readAuth());
    const [path, setPath] = useState(window.location.pathname || '/catalog');
    const [filters, setFilters] = useState<ProductFilters>(() => filtersFromUrl());
    const [products, setProducts] = useState<PagedResponse<Product> | null>(null);
    const [productsLoading, setProductsLoading] = useState(false);
    const [productsError, setProductsError] = useState('');
    const [cart, setCart] = useState<Record<number, number>>(() => readCart());
    const [knownProducts, setKnownProducts] = useState<Record<number, Product>>(() => readProductCache());
    const [orders, setOrders] = useState<PagedResponse<Order> | null>(null);
    const [adminOrders, setAdminOrders] = useState<PagedResponse<Order> | null>(null);
    const [report, setReport] = useState<CustomerReport[]>([]);
    const [reportLoaded, setReportLoaded] = useState(false);
    const [reportLoading, setReportLoading] = useState(false);
    const [reportError, setReportError] = useState('');
    const [screenLoading, setScreenLoading] = useState(false);
    const [screenError, setScreenError] = useState('');
    const [notice, setNotice] = useState('');
    const [busy, setBusy] = useState(false);
    const [registerMode, setRegisterMode] = useState(false);
    const [email, setEmail] = useState(''); const [password, setPassword] = useState(''); const [fullName, setFullName] = useState('');
    const [editing, setEditing] = useState<Product | null>(null);
    const [productForm, setProductForm] = useState({ sku: '', name: '', price: '', stockQuantity: '', isActive: true });
    const checkoutLock = useRef(false);
    const productFetches = useRef(new Set<number>());
    const [ordersPage, setOrdersPage] = useState(1);
    const [adminOrdersPage, setAdminOrdersPage] = useState(1);

    useEffect(() => { const onPop = () => { setPath(window.location.pathname); setFilters(filtersFromUrl()); }; const onExpired = () => { setAuth(null); setNotice('Your session expired. Please sign in again.'); setPath('/login'); window.history.replaceState({}, '', '/login'); }; window.addEventListener('popstate', onPop); window.addEventListener('oi-session-expired', onExpired); return () => { window.removeEventListener('popstate', onPop); window.removeEventListener('oi-session-expired', onExpired); }; }, []);
    useEffect(() => { localStorage.setItem(CART_KEY, JSON.stringify(cart)); }, [cart]);
    useEffect(() => { localStorage.setItem(PRODUCT_CACHE_KEY, JSON.stringify(knownProducts)); }, [knownProducts]);
    useEffect(() => { if (auth) writeAuth(auth); }, [auth]);
    useEffect(() => {
        const params = new URLSearchParams();
        (Object.keys(filters) as (keyof ProductFilters)[]).forEach(key => { const value = filters[key]; if (value !== '' && value !== defaultFilters[key]) params.set(key, String(value)); });
        const query = params.toString(); const next = `/catalog${query ? `?${query}` : ''}`;
        if (path === '/catalog' && window.location.search !== (query ? `?${query}` : '')) window.history.replaceState({}, '', next);
    }, [filters, path]);

    const loadProducts = useCallback(async (current: ProductFilters) => {
        setProductsLoading(true); setProductsError('');
        try { const result = await productApi.list(current); setProducts(result); setKnownProducts(cache => ({ ...cache, ...Object.fromEntries(result.items.map(p => [p.id, p])) })); }
        catch (error) { setProductsError(getError(error)); }
        finally { setProductsLoading(false); }
    }, []);
    useEffect(() => { if (path === '/catalog' || path === '/' || path === '/admin') { const timer = window.setTimeout(() => void loadProducts(filters), filters.search ? 300 : 0); return () => window.clearTimeout(timer); } }, [filters, path, loadProducts]);

    const loadMyOrders = useCallback(async (page = ordersPage) => { setScreenLoading(true); setScreenError(''); try { setOrders(await orderApi.mine(page)); } catch (e) { setScreenError(getError(e)); } finally { setScreenLoading(false); } }, [ordersPage]);
    const loadAdminOrders = useCallback(async (page = adminOrdersPage) => { setScreenLoading(true); setScreenError(''); try { setAdminOrders(await orderApi.all(page)); } catch (e) { setScreenError(getError(e)); } finally { setScreenLoading(false); } }, [adminOrdersPage]);
    useEffect(() => { if (path === '/orders' && auth?.role === 'Customer') void loadMyOrders(ordersPage); }, [path, auth?.role, ordersPage, loadMyOrders]);
    useEffect(() => { if (path === '/admin' && auth?.role === 'Admin') { void loadAdminOrders(adminOrdersPage); void loadTopCustomers(); } }, [path, auth?.role, adminOrdersPage, loadAdminOrders]);

    async function loadTopCustomers() { setReportLoading(true); setReportError(''); try { setReport(await reportApi.topCustomers(30)); setReportLoaded(true); } catch (e) { setReportError(getError(e)); } finally { setReportLoading(false); } }
    function updateFilters(patch: Partial<ProductFilters>) { setFilters(current => ({ ...current, ...patch, ...(patch.pageNumber === undefined ? { pageNumber: 1 } : {}) })); }
    function changeRoute(next: string) { navigate(next); setNotice(''); setScreenError(''); }
    function logout() { setAuth(null); writeAuth(null); setOrders(null); setAdminOrders(null); setCart({}); localStorage.removeItem(CART_KEY); localStorage.removeItem('oi-checkout-key'); changeRoute('/login'); }

    async function authenticate(event: FormEvent) {
        event.preventDefault(); setBusy(true); setNotice('');
        try {
            if (registerMode) await authApi.register(fullName, email, password);
            const result = await authApi.login(email, password); setAuth(result); writeAuth(result); setNotice(`Welcome, ${result.fullName}.`); changeRoute(result.role === 'Admin' ? '/admin' : '/catalog');
        } catch (error) { setNotice(getError(error)); }
        finally { setBusy(false); }
    }
    const cartItems = useMemo(() => Object.entries(cart).filter(([, quantity]) => quantity > 0).map(([id, quantity]) => ({ productId: Number(id), quantity })), [cart]);
    const subtotal = cartItems.reduce((sum, item) => sum + (knownProducts[item.productId]?.price || 0) * item.quantity, 0);
    useEffect(() => { if (path === '/cart') { const missing = cartItems.map(i => i.productId).filter(id => !knownProducts[id] && !productFetches.current.has(id)); missing.forEach(id => productFetches.current.add(id)); if (missing.length) void Promise.all(missing.map(id => productApi.get(id).catch(() => null))).then(found => setKnownProducts(cache => ({ ...cache, ...Object.fromEntries(found.filter((p): p is Product => !!p).map(p => [p.id, p])) }))); } }, [path, cartItems, knownProducts]);
    async function checkout() {
        if (!auth) { changeRoute('/login'); return; }
        if (!cartItems.length || checkoutLock.current) return;
        checkoutLock.current = true; setBusy(true); setNotice('');
        try {
            const cartSignature = JSON.stringify([...cartItems].sort((a, b) => a.productId - b.productId));
            const keyStore = 'oi-checkout-key';
            let saved = ''; try { saved = JSON.parse(localStorage.getItem(keyStore) || '{}').signature === cartSignature ? JSON.parse(localStorage.getItem(keyStore) || '{}').key : ''; } catch { saved = ''; }
            const key = saved || (crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(36).slice(2)}`);
            localStorage.setItem(keyStore, JSON.stringify({ signature: cartSignature, key }));
            await orderApi.place(cartItems, key); setCart({}); localStorage.removeItem(keyStore); setNotice('Order placed successfully. Inventory has been reserved.'); changeRoute('/orders');
        } catch (error) { setNotice(getError(error)); }
        finally { setBusy(false); checkoutLock.current = false; }
    }
    async function cancelOrder(order: Order) {
        try { await orderApi.changeStatus(order.id, 'Cancelled'); setNotice(`Order #${order.id} cancelled.`); await loadMyOrders(ordersPage); void loadProducts(filters); }
        catch (e) { setScreenError(getError(e)); }
    }
    function startProductEdit(product: Product) { setEditing(product); setProductForm({ sku: product.sku, name: product.name, price: String(product.price), stockQuantity: String(product.stockQuantity), isActive: product.isActive }); }
    function startProductCreate() { setEditing(null); setProductForm({ sku: '', name: '', price: '', stockQuantity: '', isActive: true }); }
    async function saveProduct(event: FormEvent) {
        event.preventDefault(); setBusy(true); setScreenError('');
        const data = { sku: productForm.sku, name: productForm.name, price: Number(productForm.price), stockQuantity: Number(productForm.stockQuantity), isActive: productForm.isActive };
        try {
            if (editing) await productApi.update(editing.id, { ...data, rowVersion: editing.rowVersion }); else await productApi.create(data);
            setNotice(editing ? 'Product updated.' : 'Product created.'); startProductCreate(); void loadProducts(filters);
        } catch (e) { setScreenError(getError(e)); }
        finally { setBusy(false); }
    }
    async function changeAdminStatus(order: Order, status: string) {
        try { await orderApi.changeStatus(order.id, status); setNotice(`Order #${order.id} moved to ${status}.`); await loadAdminOrders(adminOrdersPage); }
        catch (e) { setScreenError(getError(e)); }
    }

    const currentTitle = path === '/admin' ? 'Admin workspace' : path === '/orders' ? 'My orders' : path === '/cart' ? 'Cart & checkout' : 'Product catalog';
    return <div className="app-shell">
        <header className="topbar"><button className="brand" onClick={() => changeRoute('/catalog')}><span className="brand-mark">OI</span><span><b>Order & Inventory</b><small>STORE OPERATIONS</small></span></button>
            <nav className="main-nav" aria-label="Main navigation"><button className={path === '/catalog' ? 'active' : ''} onClick={() => changeRoute('/catalog')}>Catalog</button>{auth?.role === 'Customer' && <><button className={path === '/cart' ? 'active' : ''} onClick={() => changeRoute('/cart')}>Cart <span className="nav-count">{cartItems.reduce((s, i) => s + i.quantity, 0)}</span></button><button className={path === '/orders' ? 'active' : ''} onClick={() => changeRoute('/orders')}>My orders</button></>}{auth?.role === 'Admin' && <button className={path === '/admin' ? 'active' : ''} onClick={() => changeRoute('/admin')}>Admin</button>}</nav>
            <div className="account">{auth ? <><span className="account-name">{auth.fullName}<small>{auth.role}</small></span><button className="button secondary small" onClick={logout}>Sign out</button></> : <button className="button small" onClick={() => changeRoute('/login')}>Sign in</button>}</div>
        </header>
        <main className="page"><div className="page-heading"><div><span className="kicker">ORDER INVENTORY MANAGEMENT</span><h1>{currentTitle}</h1>
        </div>{path !== '/login' && <span className="api-status"><i /> API connected through /api</span>}</div>
            {notice && <div className="notice" role="status">{notice}<button aria-label="Dismiss" onClick={() => setNotice('')}>×</button></div>}
            {(path === '/login' || (!auth && path === '/')) && <section className="auth-layout"><div className="auth-intro"><span className="kicker">{registerMode ? 'JOIN THE WORKSPACE' : 'WELCOME BACK'}</span><h2>{registerMode ? 'Create your account.' : 'Everything in stock.\nEvery order in control.'}</h2><p>Sign in to browse products, reserve inventory and keep track of every order.</p></div><form className="panel form-card" onSubmit={authenticate}><h2>{registerMode ? 'Create account' : 'Sign in'}</h2>{registerMode && <label>Full name<input required minLength={2} value={fullName} onChange={e => setFullName(e.target.value)} placeholder="Your name" /></label>}<label>Email address<input required type="email" value={email} onChange={e => setEmail(e.target.value)} placeholder="you@example.com" /></label><label>Password<input required minLength={8} type="password" value={password} onChange={e => setPassword(e.target.value)} placeholder="At least 8 characters" /></label><button className="button full" disabled={busy}>{busy ? 'Please wait…' : registerMode ? 'Create account' : 'Sign in'}</button><button type="button" className="link-button" onClick={() => setRegisterMode(v => !v)}>{registerMode ? 'Already registered? Sign in' : 'New here? Create an account'}</button></form></section>}

            {(path === '/catalog' || path === '/') && <section>
                <div className="filter-panel panel"><label className="search-field"><span>Search products</span><input value={filters.search} onChange={e => updateFilters({ search: e.target.value })} placeholder="Search by product name…" /></label><label><span>Min price (₹)</span><input type="number" min="0" value={filters.minPrice} onChange={e => updateFilters({ minPrice: e.target.value })} placeholder="0" /></label><label><span>Max price (₹)</span><input type="number" min="0" value={filters.maxPrice} onChange={e => updateFilters({ maxPrice: e.target.value })} placeholder="Any" /></label><label><span>Availability</span><select value={filters.inStock} onChange={e => updateFilters({ inStock: e.target.value })}><option value="">All stock</option><option value="true">In stock</option><option value="false">Out of stock</option></select></label><button className="button secondary clear-filters" onClick={() => setFilters(defaultFilters)}>Clear filters</button></div>
                <div className="results-bar"><span>{products ? `${products.totalCount} products found` : 'Browse available products'}</span><label>Sort <select value={`${filters.sortBy}:${filters.sortDirection}`} onChange={e => { const [sortBy, sortDirection] = e.target.value.split(':'); updateFilters({ sortBy, sortDirection }); }}><option value="name:asc">Name A–Z</option><option value="price:asc">Price: low to high</option><option value="price:desc">Price: high to low</option></select></label></div>
                {productsLoading ? <Loading label="Loading products…" /> : productsError ? <ErrorState message={productsError} retry={() => void loadProducts(filters)} /> : products?.items.length ? <><div className="product-grid">{products.items.filter(p => p.isActive).map(product => <article className="product-card panel" key={product.id}><div className="product-meta"><span className="sku">{product.sku}</span><span className={product.stockQuantity > 0 ? 'stock-pill available' : 'stock-pill unavailable'}>{product.stockQuantity > 0 ? `${product.stockQuantity} in stock` : 'Out of stock'}</span></div><h3>{product.name}</h3><div className="product-price">{money(product.price)}</div><div className="product-actions">{auth?.role === 'Customer' ? <button className="button" disabled={product.stockQuantity < 1} onClick={() => setCart(current => ({ ...current, [product.id]: Math.min(product.stockQuantity, (current[product.id] || 0) + 1) }))}>Add to cart</button> : <button className="button secondary" onClick={() => changeRoute('/login')}>Sign in to order</button>}<span>{cart[product.id] ? `${cart[product.id]} in cart` : ' '}</span></div></article>)}</div><Pagination page={products.pageNumber} totalPages={products.totalPages} onChange={pageNumber => updateFilters({ pageNumber })} /></> : <Empty title="No products found" detail="Try adjusting your search or filters." />}
            </section>}

            {path === '/cart' && <Guard role="Customer" auth={auth}><section className="panel cart-panel"><div className="section-title"><div><span className="kicker">READY WHEN YOU ARE</span><h2>Your basket</h2></div><button className="button secondary" onClick={() => changeRoute('/catalog')}>Continue shopping</button></div>{cartItems.length ? <><div className="cart-lines">{cartItems.map(item => { const product = knownProducts[item.productId]; return <div className="cart-line" key={item.productId}><div><strong>{product?.name || `Product #${item.productId}`}</strong><small>{product ? money(product.price) : 'Price will be confirmed by the server'}</small></div><div className="quantity-control"><button aria-label="Decrease quantity" onClick={() => setCart(c => ({ ...c, [item.productId]: Math.max(0, item.quantity - 1) }))}>−</button><b>{item.quantity}</b><button aria-label="Increase quantity" onClick={() => setCart(c => ({ ...c, [item.productId]: item.quantity + 1 }))}>+</button></div><button className="remove-button" onClick={() => setCart(c => ({ ...c, [item.productId]: 0 }))}>Remove</button></div>; })}</div><div className="checkout-summary"><div><small>ESTIMATED SUBTOTAL</small><strong>{money(subtotal)}</strong><p>Final pricing and any eligible discounts are calculated dynamically.</p></div><button className="button" disabled={busy || !cartItems.length} onClick={() => void checkout()}>{busy ? 'Placing order…' : 'Place order'}</button></div></> : <Empty title="Your cart is empty" detail="Add products from the catalog to get started." />}</section></Guard>}

            {path === '/orders' && <Guard role="Customer" auth={auth}><section>{screenLoading ? <Loading label="Loading your orders…" /> : screenError ? <ErrorState message={screenError} retry={() => void loadMyOrders(ordersPage)} /> : orders?.items.length ? <><div className="order-list">{orders.items.map(order => <OrderCard key={order.id} order={order} customer onCancel={() => void cancelOrder(order)} />)}</div><Pagination page={orders.pageNumber} totalPages={orders.totalPages} onChange={setOrdersPage} /></> : <Empty title="No orders yet" detail="Orders you place will appear here." />}</section></Guard>}

            {path === '/admin' && <Guard role="Admin" auth={auth}><div className="admin-layout">
                <section className="panel admin-form-panel"><div className="section-title"><div><span className="kicker">CATALOG MANAGEMENT</span><h2>{editing ? `Edit product #${editing.id}` : 'Create a product'}</h2></div>{editing && <button className="button secondary" onClick={startProductCreate}>Cancel edit</button>}</div><form className="admin-product-form" onSubmit={saveProduct}><label>SKU<input required maxLength={50} value={productForm.sku} onChange={e => setProductForm(f => ({ ...f, sku: e.target.value }))} /></label><label>Product name<input required maxLength={200} value={productForm.name} onChange={e => setProductForm(f => ({ ...f, name: e.target.value }))} /></label><label>Price (₹)<input required type="number" min="0.01" step="0.01" value={productForm.price} onChange={e => setProductForm(f => ({ ...f, price: e.target.value }))} /></label><label>Stock quantity<input required type="number" min="0" step="1" value={productForm.stockQuantity} onChange={e => setProductForm(f => ({ ...f, stockQuantity: e.target.value }))} /></label><label className="checkbox-label"><input type="checkbox" checked={productForm.isActive} onChange={e => setProductForm(f => ({ ...f, isActive: e.target.checked }))} /> Active product</label><button className="button" disabled={busy}>{busy ? 'Saving…' : editing ? 'Save changes' : 'Create product'}</button></form><div className="admin-product-list"><div className="section-title"><h3>Products</h3><button className="button secondary small" onClick={() => void loadProducts(filters)}>Refresh catalog</button></div>{productsLoading ? <Loading /> : productsError ? <ErrorState message={productsError} retry={() => void loadProducts(filters)} /> : products?.items.length ? products.items.map(p => <div className="admin-product-row" key={p.id}><span><b>{p.name}</b><small>{p.sku} · {money(p.price)} · {p.stockQuantity} stock</small></span><button className="button secondary small" onClick={() => startProductEdit(p)}>Edit</button></div>) : <Empty title="No products" detail="Create the first product above." />}</div></section>
                <section className="panel report-panel"><div className="section-title"><div><span className="kicker">CUSTOMER INSIGHTS</span><h2>Top customers</h2></div><button className="button secondary small" onClick={() => void loadTopCustomers()}>Refresh</button></div><p className="muted">Top customers by confirmed and shipped spend over the last 30 days.</p>{reportLoading ? <Loading label="Loading customer report…" /> : reportError ? <ErrorState message={reportError} retry={() => void loadTopCustomers()} /> : !reportLoaded ? <Empty title="Report not loaded" detail="Load the latest customer summary." /> : report.length ? <div className="table-wrap"><table><thead><tr><th>Customer</th><th>Orders</th><th>Total spend</th></tr></thead><tbody>{report.map(row => <tr key={row.customerId}><td><b>{row.customerName}</b><small>{row.email}</small></td><td>{row.totalOrders}</td><td>{money(row.totalSpend)}</td></tr>)}</tbody></table></div> : <Empty title="No customer data" detail="There are no qualifying orders in this period." />}</section>
                <section className="panel admin-orders-panel"><div className="section-title"><div><span className="kicker">FULFILMENT</span><h2>All orders</h2></div><button className="button secondary small" onClick={() => void loadAdminOrders(adminOrdersPage)}>Refresh</button></div>{screenLoading ? <Loading label="Loading orders…" /> : screenError ? <ErrorState message={screenError} retry={() => void loadAdminOrders(adminOrdersPage)} /> : adminOrders?.items.length ? <><div className="order-list">{adminOrders.items.map(order => <OrderCard key={order.id} order={order} admin onStatusChange={status => void changeAdminStatus(order, status)} />)}</div><Pagination page={adminOrders.pageNumber} totalPages={adminOrders.totalPages} onChange={setAdminOrdersPage} /></> : <Empty title="No orders found" detail="Customer orders will appear here." />}</section>
            </div></Guard>}
        </main>
        {/*<footer>ASP.NET Core Web API <span>·</span> Entity Framework Core <span>·</span> SQL Server <span>·</span> React + TypeScript</footer>*/}
    </div>;
}

function Pagination({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
    if (totalPages <= 1) return null;
    return <div className="pagination"><button className="button secondary small" disabled={page <= 1} onClick={() => onChange(page - 1)}>Previous</button><span>Page <b>{page}</b> of {totalPages}</span><button className="button secondary small" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>Next</button></div>;
}
function OrderCard({ order, customer, admin, onCancel, onStatusChange }: { order: Order; customer?: boolean; admin?: boolean; onCancel?: () => void; onStatusChange?: (status: string) => void }) {
    const adminOptions = order.status === 'Pending' ? ['Confirmed', 'Cancelled'] : order.status === 'Confirmed' ? ['Shipped', 'Cancelled'] : [];
    return <article className="order-card panel"><div className="order-main"><div className="order-heading"><strong>Order #{order.id}</strong><span className={`status-pill ${order.status.toLowerCase()}`}>{order.status}</span></div>{admin && <small className="customer-line">{order.customerName} · Customer #{order.customerId}</small>}<small>{new Date(order.createdAt).toLocaleString()}</small><p>{order.items.map(item => `${item.productName} × ${item.quantity}`).join(' · ')}</p></div><div className="order-side"><strong>{money(order.total)}</strong>{order.discount > 0 && <small>Includes {money(order.discount)} discount</small>}{customer && order.status === 'Pending' && <button className="link-button danger" onClick={onCancel}>Cancel order</button>}{admin && adminOptions.length > 0 && <select aria-label={`Change status for order ${order.id}`} value="" onChange={e => { if (e.target.value) onStatusChange?.(e.target.value); }}><option value="">Change status…</option>{adminOptions.map(option => <option key={option} value={option}>{option}</option>)}</select>}</div></article>;
}
