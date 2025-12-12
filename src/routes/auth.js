import express from 'express';
import { body, validationResult } from 'express-validator';
import { createSupabaseClient } from '../lib/supabase.js';
import { ensureAuthenticated, flashMessage } from '../middleware/auth.js';
import { ensureProfile, fetchProfileById, updateProfile, fetchUserSubscriptions } from '../services/profileService.js';

const router = express.Router();

router.get('/login', (req, res) => {
  res.render('login', { title: 'Login' });
});

router.post(
  '/login',
  body('email').isEmail().withMessage('Please enter a valid email address.'),
  body('password').notEmpty().withMessage('Password is required.'),
  async (req, res) => {
    const errors = validationResult(req);
    if (!errors.isEmpty()) {
      flashMessage(req, 'error', errors.array()[0].msg);
      return res.redirect('/login');
    }

    const { email, password } = req.body;
    const supabase = createSupabaseClient();
    try {
      const { data, error } = await supabase.auth.signInWithPassword({ email: email.toLowerCase(), password });

      if (error || !data?.session || !data.user) {
        flashMessage(req, 'error', 'Invalid credentials.');
        return res.redirect('/login');
      }

      await supabase.auth.setSession(data.session);
      const profile = await ensureProfile(
        { userId: data.user.id, fullName: data.user.user_metadata?.full_name || '', marketingOptIn: false },
        supabase
      );

      req.session.supabase = {
        access_token: data.session.access_token,
        refresh_token: data.session.refresh_token
      };
      req.session.user = {
        id: data.user.id,
        email: data.user.email,
        fullName: profile?.full_name,
        marketingOptIn: Boolean(profile?.marketing_opt_in),
        role: profile?.role || 'user'
      };
    } catch (err) {
      flashMessage(req, 'error', 'Unable to sign in right now. Please try again.');
      return res.redirect('/login');
    }

    const redirectTo = req.session.returnTo || '/account';
    delete req.session.returnTo;
    return res.redirect(redirectTo);
  }
);

router.get('/register', (req, res) => {
  res.render('register', { title: 'Create account' });
});

router.post(
  '/register',
  body('email').isEmail().withMessage('Please enter a valid email address.'),
  body('password').isLength({ min: 8 }).withMessage('Password must be at least 8 characters.'),
  body('fullName').trim().notEmpty().withMessage('Name is required.'),
  async (req, res) => {
    const errors = validationResult(req);
    if (!errors.isEmpty()) {
      flashMessage(req, 'error', errors.array()[0].msg);
      return res.redirect('/register');
    }

    const { email, password, fullName } = req.body;
    const supabase = createSupabaseClient();

    try {
      const { data, error } = await supabase.auth.signUp({ email: email.toLowerCase(), password });
      if (error) {
        flashMessage(req, 'error', error.message);
        return res.redirect('/register');
      }

      if (!data?.user || !data.session) {
        flashMessage(req, 'success', 'Account created. Check your email to confirm your address before logging in.');
        return res.redirect('/login');
      }

      await supabase.auth.setSession(data.session);
      await ensureProfile({ userId: data.user.id, fullName, marketingOptIn: false }, supabase);

      req.session.supabase = {
        access_token: data.session.access_token,
        refresh_token: data.session.refresh_token
      };
      req.session.user = {
        id: data.user.id,
        email: data.user.email,
        fullName,
        marketingOptIn: false,
        role: 'user'
      };
    } catch (err) {
      flashMessage(req, 'error', 'Unable to create your account right now. Please try again.');
      return res.redirect('/register');
    }

    flashMessage(req, 'success', 'Account created. You are now signed in.');
    return res.redirect('/account');
  }
);

router.post('/logout', (req, res) => {
  req.session.destroy(() => {
    res.redirect('/');
  });
});

router.get('/account', ensureAuthenticated, async (req, res) => {
  try {
    const supabase =
      req.supabase || createSupabaseClient(req.session.supabase?.access_token, req.session.supabase?.refresh_token);
    const profile = req.profile || (await fetchProfileById(req.session.user.id, supabase));
    const subscriptions = await fetchUserSubscriptions(req.session.user.id, supabase);

    res.render('account', {
      title: 'Your account',
      profile,
      email: req.session.user.email,
      subscriptions
    });
  } catch (error) {
    flashMessage(req, 'error', 'Unable to load your profile right now. Please try again.');
    return res.redirect('/');
  }
});

router.post(
  '/account',
  ensureAuthenticated,
  body('fullName').trim().notEmpty().withMessage('Name is required.'),
  async (req, res) => {
    const errors = validationResult(req);
    if (!errors.isEmpty()) {
      flashMessage(req, 'error', errors.array()[0].msg);
      return res.redirect('/account');
    }

    const { fullName, marketingOptIn } = req.body;
    try {
      const supabase =
        req.supabase || createSupabaseClient(req.session.supabase?.access_token, req.session.supabase?.refresh_token);

      const updated = await updateProfile(
        req.session.user.id,
        {
          fullName,
          marketingOptIn: marketingOptIn === 'on'
        },
        supabase
      );

      req.session.user = {
        ...req.session.user,
        fullName: updated.full_name,
        marketingOptIn: Boolean(updated.marketing_opt_in),
        role: updated.role
      };
    } catch (error) {
      flashMessage(req, 'error', 'Could not update your profile. Please try again.');
      return res.redirect('/account');
    }

    flashMessage(req, 'success', 'Profile updated.');
    return res.redirect('/account');
  }
);

export default router;
