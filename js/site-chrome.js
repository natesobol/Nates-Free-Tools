(() => {
  const pathSegments = window.location.pathname.split('/').filter(Boolean);
  const repoIndex = pathSegments.indexOf('Nates-Free-Tools');
  const basePath = repoIndex !== -1 ? `/${pathSegments.slice(0, repoIndex + 1).join('/')}` : '';

  const loadScript = (src, datasetName) => {
    const attribute = `data-${datasetName}`;
    const filename = src.split('/').pop();
    const alreadyLoaded =
      document.querySelector(`script[${attribute}]`) ||
      Array.from(document.scripts).some(script => (script.getAttribute('src') || '').includes(filename));

    if (alreadyLoaded) {
      return;
    }

    const script = document.createElement('script');
    script.src = src;
    script.setAttribute(attribute, 'true');
    document.head.appendChild(script);
  };

  loadScript(`${basePath}/public/js/webapps-menu.js`, 'proxied-webapps-menu');
  loadScript(`${basePath}/public/js/site-chrome.js`, 'proxied-site-chrome');
})();
