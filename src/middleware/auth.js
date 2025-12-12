import { createSupabaseClient } from '../lib/supabase.js';
import { fetchProfileById } from '../services/profileService.js';

export async function attachSupabase(req, res, next) {
  const tokens = req.session?.supabase;
  req.supabase = createSupabaseClient(tokens?.access_token, tokens?.refresh_token);

  res.locals.flash = req.session.flash;
  delete req.session.flash;

  if (tokens?.access_token && tokens?.refresh_token) {
    try {
      const { data, error } = await req.supabase.auth.getUser();
      if (error || !data?.user) {
        delete req.session.supabase;
        delete req.session.user;
      } else {
        const profile = await fetchProfileById(data.user.id, req.supabase);
        if (profile) {
          req.session.user = {
            id: data.user.id,
            email: data.user.email,
            fullName: profile.full_name,
            marketingOptIn: Boolean(profile.marketing_opt_in),
            role: profile.role || 'user'
          };
          req.profile = profile;
        }
      }
    } catch (err) {
      delete req.session.supabase;
      delete req.session.user;
    }
  }

  res.locals.currentUser = req.session.user;
  next();
}

export function ensureAuthenticated(req, res, next) {
  if (req.session.user) {
    return next();
  }
  req.session.returnTo = req.originalUrl;
  return res.redirect('/login');
}

export function ensureAdmin(req, res, next) {
  if (req.session.user?.role === 'admin') {
    return next();
  }

  if (req.session.user) {
    return res.status(403).render('404', { title: 'Forbidden' });
  }

  req.session.returnTo = req.originalUrl;
  return res.redirect('/login');
}

export function flashMessage(req, type, message) {
  req.session.flash = { type, message };
}
