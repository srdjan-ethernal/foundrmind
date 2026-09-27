window.fm = {
  copy: (text) => navigator.clipboard.writeText(text),
  download: (name, text, mime) => {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type: mime || 'text/plain' }));
    a.download = name; a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  },
  scrollBottom: (el) => { if (el) el.scrollTop = el.scrollHeight; },
  tzOffset: () => new Date().getTimezoneOffset()
};
