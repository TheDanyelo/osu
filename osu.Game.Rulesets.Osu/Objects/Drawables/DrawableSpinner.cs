﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects.Drawables.Pieces;
using osuTK;
using osuTK.Graphics;
using osu.Game.Graphics;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.Objects;
using osu.Framework.Utils;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Osu.Objects.Drawables
{
    public class DrawableSpinner : DrawableOsuHitObject
    {
        protected readonly Spinner Spinner;

        private readonly Container<DrawableSpinnerTick> ticks;

        public readonly SpinnerDisc Disc;
        public readonly SpinnerTicks Ticks;
        public readonly SpinnerSpmCounter SpmCounter;
        private readonly SpinnerBonusDisplay bonusDisplay;

        private readonly Container mainContainer;

        public readonly SkinnableDrawable Background;
        private readonly SkinnableDrawable circleContainer;

        private readonly Color4 baseColour = Color4Extensions.FromHex(@"002c3c");
        private readonly Color4 fillColour = Color4Extensions.FromHex(@"005b7c");

        private readonly IBindable<Vector2> positionBindable = new Bindable<Vector2>();

        private Color4 normalColour;
        private Color4 completeColour;

        public DrawableSpinner(Spinner s)
            : base(s)
        {
            Origin = Anchor.Centre;
            Position = s.Position;

            RelativeSizeAxes = Axes.Both;

            // we are slightly bigger than our parent, to clip the top and bottom of the circle
            Height = 1.3f;

            Spinner = s;

            InternalChildren = new Drawable[]
            {
                ticks = new Container<DrawableSpinnerTick>(),
                circleContainer = new SkinnableDrawable(new OsuSkinComponent(OsuSkinComponents.SpinnerCentre), _ => new DefaultSpinnerCentre())
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                },
                mainContainer = new AspectContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Children = new[]
                    {
                        Background = new SkinnableDrawable(new OsuSkinComponent(OsuSkinComponents.SpinnerBackground), _ => new DefaultSpinnerBackground()),
                        Disc = new SpinnerDisc(Spinner)
                        {
                            Scale = Vector2.Zero,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                        },
                        circleContainer.CreateProxy(),
                        Ticks = new SpinnerTicks
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                        },
                    }
                },
                SpmCounter = new SpinnerSpmCounter
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Y = 120,
                    Alpha = 0
                },
                bonusDisplay = new SpinnerBonusDisplay
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Y = -120,
                }
            };
        }

        protected override void AddNestedHitObject(DrawableHitObject hitObject)
        {
            base.AddNestedHitObject(hitObject);

            switch (hitObject)
            {
                case DrawableSpinnerTick tick:
                    ticks.Add(tick);
                    break;
            }
        }

        protected override void ClearNestedHitObjects()
        {
            base.ClearNestedHitObjects();
            ticks.Clear();
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject)
        {
            switch (hitObject)
            {
                case SpinnerBonusTick bonusTick:
                    return new DrawableSpinnerBonusTick(bonusTick);

                case SpinnerTick tick:
                    return new DrawableSpinnerTick(tick);
            }

            return base.CreateNestedHitObject(hitObject);
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            normalColour = baseColour;
            completeColour = colours.YellowLight;

            Ticks.AccentColour = normalColour;
            Disc.AccentColour = fillColour;

            positionBindable.BindValueChanged(pos => Position = pos.NewValue);
            positionBindable.BindTo(HitObject.PositionBindable);
        }

        public float Progress => Math.Clamp(Disc.CumulativeRotation / 360 / Spinner.SpinsRequired, 0, 1);

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (Time.Current < HitObject.StartTime) return;

            if (Progress >= 1 && !Disc.Complete)
            {
                Disc.Complete = true;
                transformFillColour(completeColour, 200);
            }

            if (userTriggered || Time.Current < Spinner.EndTime)
                return;

            // Trigger a miss result for remaining ticks to avoid infinite gameplay.
            foreach (var tick in ticks.Where(t => !t.IsHit))
                tick.TriggerResult(false);

            ApplyResult(r =>
            {
                if (Progress >= 1)
                    r.Type = HitResult.Great;
                else if (Progress > .9)
                    r.Type = HitResult.Good;
                else if (Progress > .75)
                    r.Type = HitResult.Meh;
                else if (Time.Current >= Spinner.EndTime)
                    r.Type = HitResult.Miss;
            });
        }

        protected override void Update()
        {
            base.Update();
            if (HandleUserInput)
                Disc.Tracking = OsuActionInputManager?.PressedActions.Any(x => x == OsuAction.LeftButton || x == OsuAction.RightButton) ?? false;
        }

        private float relativeHeight => ToScreenSpace(new RectangleF(0, 0, OsuHitObject.OBJECT_RADIUS, OsuHitObject.OBJECT_RADIUS)).Height / mainContainer.DrawHeight;

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            if (!SpmCounter.IsPresent && Disc.Tracking)
                SpmCounter.FadeIn(HitObject.TimeFadeIn);

            Ticks.Rotation = Disc.Rotation;

            SpmCounter.SetRotation(Disc.CumulativeRotation);

            updateBonusScore();

            float relativeCircleScale = Spinner.Scale * relativeHeight;
            float targetScale = relativeCircleScale + (1 - relativeCircleScale) * Progress;
            Disc.Scale = new Vector2((float)Interpolation.Lerp(Disc.Scale.X, targetScale, Math.Clamp(Math.Abs(Time.Elapsed) / 100, 0, 1)));
        }

        private int wholeSpins;

        private void updateBonusScore()
        {
            if (ticks.Count == 0)
                return;

            int spins = (int)(Disc.CumulativeRotation / 360);

            if (spins < wholeSpins)
            {
                // rewinding, silently handle
                wholeSpins = spins;
                return;
            }

            while (wholeSpins != spins)
            {
                var tick = ticks.FirstOrDefault(t => !t.IsHit);

                // tick may be null if we've hit the spin limit.
                if (tick != null)
                {
                    tick.TriggerResult(true);
                    if (tick is DrawableSpinnerBonusTick)
                        bonusDisplay.SetBonusCount(spins - Spinner.SpinsRequired);
                }

                wholeSpins++;
            }
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            circleContainer.ScaleTo(0);
            mainContainer.ScaleTo(0);

            using (BeginDelayedSequence(HitObject.TimePreempt / 2, true))
            {
                float phaseOneScale = Spinner.Scale * 0.7f;

                circleContainer.ScaleTo(phaseOneScale, HitObject.TimePreempt / 4, Easing.OutQuint);

                mainContainer
                    .ScaleTo(phaseOneScale * relativeHeight * 1.6f, HitObject.TimePreempt / 4, Easing.OutQuint)
                    .RotateTo((float)(25 * Spinner.Duration / 2000), HitObject.TimePreempt + Spinner.Duration);

                using (BeginDelayedSequence(HitObject.TimePreempt / 2, true))
                {
                    circleContainer.ScaleTo(Spinner.Scale, 400, Easing.OutQuint);
                    mainContainer.ScaleTo(1, 400, Easing.OutQuint);
                }
            }
        }

        protected override void UpdateStateTransforms(ArmedState state)
        {
            base.UpdateStateTransforms(state);

            using (BeginDelayedSequence(Spinner.Duration, true))
            {
                this.FadeOut(160);

                switch (state)
                {
                    case ArmedState.Hit:
                        transformFillColour(completeColour, 0);
                        this.ScaleTo(Scale * 1.2f, 320, Easing.Out);
                        mainContainer.RotateTo(mainContainer.Rotation + 180, 320);
                        break;

                    case ArmedState.Miss:
                        this.ScaleTo(Scale * 0.8f, 320, Easing.In);
                        break;
                }
            }
        }

        private void transformFillColour(Colour4 colour, double duration)
        {
            Disc.FadeAccent(colour, duration);
            Ticks.FadeAccent(colour, duration);
        }
    }
}
