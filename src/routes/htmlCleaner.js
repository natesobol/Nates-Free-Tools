import express from 'express';
import multer from 'multer';
import sanitizeHtml from 'sanitize-html';

const upload = multer({ storage: multer.memoryStorage() });
const router = express.Router();

const parseAllowlist = raw => {
  if (!raw || !raw.trim()) return [];
  return raw
    .split(/[,
\s]+/u)
    .map(tag => tag.replace(/^</, '').replace(/>$/, '').trim().toLowerCase())
    .filter(Boolean);
};

const parseBool = (value, fallback) => {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'string') {
    if (value.toLowerCase() === 'true') return true;
    if (value.toLowerCase() === 'false') return false;
  }
  return fallback;
};

const collapseWhitespace = value => value.replace(/[\t ]+/g, ' ').replace(/\n{3,}/g, '\n\n').trim();
const normalizePlainText = value => value.replace(/\s+/g, ' ').trim();

const cleanHtml = (html, allowedTags, shouldCollapse) => {
  if (!allowedTags.length) {
    const textOnly = sanitizeHtml(html, { allowedTags: [], allowedAttributes: {} });
    const normalized = normalizePlainText(textOnly);
    return shouldCollapse ? normalizePlainText(normalized) : textOnly.trim();
  }

  const cleaned = sanitizeHtml(html, {
    allowedTags: allowedTags,
    allowedAttributes: {},
    textFilter: text => text
  });

  return shouldCollapse ? collapseWhitespace(cleaned) : cleaned.trim();
};

router.post('/api/clean', upload.array('file'), (req, res) => {
  const allowedTags = parseAllowlist(req.body?.allowedTags || '');
  const collapseFlag = parseBool(req.body?.collapseWhitespace, true);
  const results = [];
  const htmlInput = req.body?.html;

  if (htmlInput && htmlInput.trim()) {
    const cleaned = cleanHtml(htmlInput, allowedTags, collapseFlag);
    results.push({
      source: 'text',
      lengthBefore: htmlInput.length,
      lengthAfter: cleaned.length,
      cleaned,
      allowedTags
    });
  }

  (req.files || []).forEach(file => {
    if (!file || !file.buffer) {
      results.push({ source: file?.originalname || 'upload', error: 'File was empty.' });
      return;
    }

    try {
      const content = file.buffer.toString('utf8');
      if (!content.length) {
        results.push({ source: file.originalname, error: 'File was empty.' });
        return;
      }
      const cleaned = cleanHtml(content, allowedTags, collapseFlag);
      results.push({
        source: file.originalname,
        lengthBefore: content.length,
        lengthAfter: cleaned.length,
        cleaned,
        allowedTags
      });
    } catch (error) {
      results.push({ source: file.originalname, error: `Failed to read file: ${error.message}` });
    }
  });

  if (!results.length) {
    return res.status(400).json({ error: 'Provide HTML in the text area or upload at least one file.' });
  }

  return res.json({
    mode: allowedTags.length ? 'allowlist' : 'strip-all',
    allowed: allowedTags,
    collapseWhitespace: collapseFlag,
    results
  });
});

export default router;
