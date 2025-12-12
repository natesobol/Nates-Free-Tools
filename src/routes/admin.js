import express from 'express';
import { ensureAdmin } from '../middleware/auth.js';
import { listProfiles } from '../services/profileService.js';

const router = express.Router();

router.get('/admin', ensureAdmin, async (req, res) => {
  try {
    const users = await listProfiles(req.supabase);
    res.render('admin', {
      title: 'Admin dashboard',
      users
    });
  } catch (error) {
    console.error('Failed to load profiles', error);
    res.status(500).render('admin', {
      title: 'Admin dashboard',
      users: []
    });
  }
});

export default router;
