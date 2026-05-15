// SkyMesh — Preetham analytic sky for the WebGPU renderer.
//
// Sourced verbatim from three.js r184 (examples/jsm/objects/SkyMesh.js).
// Bundled locally because the three.js npm addons aren't pulled in — only the
// `three.webgpu.min.js` / `three.tsl.min.js` bundles ship with the desktop app.

import {
	BackSide,
	BoxGeometry,
	Mesh,
	Vector3,
	NodeMaterial
} from 'three/webgpu';

import { Fn, float, vec2, vec3, acos, add, mul, clamp, cos, dot, exp, max, mix, modelViewProjection, normalize, positionWorld, pow, smoothstep, sub, varyingProperty, vec4, uniform, cameraPosition, fract, floor, sin, time, Loop, If } from 'three/tsl';

class SkyMesh extends Mesh {

	constructor() {

		const material = new NodeMaterial();

		super( new BoxGeometry( 1, 1, 1 ), material );

		this.turbidity = uniform( 2 );
		this.rayleigh = uniform( 1 );
		this.mieCoefficient = uniform( 0.005 );
		this.mieDirectionalG = uniform( 0.8 );
		this.sunPosition = uniform( new Vector3() );
		this.upUniform = uniform( new Vector3( 0, 1, 0 ) );
		this.cloudScale = uniform( 0.0002 );
		this.cloudSpeed = uniform( 0.0001 );
		this.cloudCoverage = uniform( 0.4 );
		this.cloudDensity = uniform( 0.4 );
		this.cloudElevation = uniform( 0.5 );
		this.showSunDisc = uniform( 1 );

		this.isSky = true; // @deprecated, r182
		this.isSkyMesh = true;

		const vSunDirection = varyingProperty( 'vec3' );
		const vSunE = varyingProperty( 'float' );
		const vBetaR = varyingProperty( 'vec3' );
		const vBetaM = varyingProperty( 'vec3' );

		const vertexNode = /*@__PURE__*/ Fn( () => {

			const e = float( 2.718281828459045 );
			const totalRayleigh = vec3( 5.804542996261093E-6, 1.3562911419845635E-5, 3.0265902468824876E-5 );
			const MieConst = vec3( 1.8399918514433978E14, 2.7798023919660528E14, 4.0790479543861094E14 );

			const cutoffAngle = float( 1.6110731556870734 );
			const steepness = float( 1.5 );
			const EE = float( 1000.0 );

			const sunDirection = normalize( this.sunPosition );
			vSunDirection.assign( sunDirection );

			const angle = dot( sunDirection, this.upUniform );
			const zenithAngleCos = clamp( angle, - 1, 1 );
			const sunIntensity = EE.mul( max( 0.0, float( 1.0 ).sub( pow( e, cutoffAngle.sub( acos( zenithAngleCos ) ).div( steepness ).negate() ) ) ) );
			vSunE.assign( sunIntensity );

			const sunfade = float( 1.0 ).sub( clamp( float( 1.0 ).sub( exp( this.sunPosition.y.div( 450000.0 ) ) ), 0, 1 ) );

			const rayleighCoefficient = this.rayleigh.sub( float( 1.0 ).mul( float( 1.0 ).sub( sunfade ) ) );

			vBetaR.assign( totalRayleigh.mul( rayleighCoefficient ) );

			const c = float( 0.2 ).mul( this.turbidity ).mul( 10E-18 );
			const totalMie = float( 0.434 ).mul( c ).mul( MieConst );

			vBetaM.assign( totalMie.mul( this.mieCoefficient ) );

			const position = modelViewProjection;
			position.z.assign( position.w ); // set z to camera.far

			return position;

		} )();

		const colorNode = /*@__PURE__*/ Fn( () => {

			const pi = float( 3.141592653589793 );

			const rayleighZenithLength = float( 8.4E3 );
			const mieZenithLength = float( 1.25E3 );
			const sunAngularDiameterCos = float( 0.9999566769464484 );

			const THREE_OVER_SIXTEENPI = float( 0.05968310365946075 );
			const ONE_OVER_FOURPI = float( 0.07957747154594767 );

			const direction = normalize( positionWorld.sub( cameraPosition ) );

			const zenithAngle = acos( max( 0.0, dot( this.upUniform, direction ) ) );
			const inverse = float( 1.0 ).div( cos( zenithAngle ).add( float( 0.15 ).mul( pow( float( 93.885 ).sub( zenithAngle.mul( 180.0 ).div( pi ) ), - 1.253 ) ) ) );
			const sR = rayleighZenithLength.mul( inverse );
			const sM = mieZenithLength.mul( inverse );

			const Fex = exp( mul( vBetaR, sR ).add( mul( vBetaM, sM ) ).negate() );

			const cosTheta = dot( direction, vSunDirection );

			const c = cosTheta.mul( 0.5 ).add( 0.5 );
			const rPhase = THREE_OVER_SIXTEENPI.mul( float( 1.0 ).add( pow( c, 2.0 ) ) );
			const betaRTheta = vBetaR.mul( rPhase );

			const g2 = pow( this.mieDirectionalG, 2.0 );
			const inv = float( 1.0 ).div( pow( float( 1.0 ).sub( float( 2.0 ).mul( this.mieDirectionalG ).mul( cosTheta ) ).add( g2 ), 1.5 ) );
			const mPhase = ONE_OVER_FOURPI.mul( float( 1.0 ).sub( g2 ) ).mul( inv );
			const betaMTheta = vBetaM.mul( mPhase );

			const Lin = pow( vSunE.mul( add( betaRTheta, betaMTheta ).div( add( vBetaR, vBetaM ) ) ).mul( sub( 1.0, Fex ) ), vec3( 1.5 ) );
			Lin.mulAssign( mix( vec3( 1.0 ), pow( vSunE.mul( add( betaRTheta, betaMTheta ).div( add( vBetaR, vBetaM ) ) ).mul( Fex ), vec3( 1.0 / 2.0 ) ), clamp( pow( sub( 1.0, dot( this.upUniform, vSunDirection ) ), 5.0 ), 0.0, 1.0 ) ) );

			const L0 = vec3( 0.1 ).mul( Fex );

			const sundisc = smoothstep( sunAngularDiameterCos, sunAngularDiameterCos.add( 0.00002 ), cosTheta ).mul( this.showSunDisc );
			L0.addAssign( vSunE.mul( 19000.0 ).mul( Fex ).mul( sundisc ) );

			const texColor = add( Lin, L0 ).mul( 0.04 ).add( vec3( 0.0, 0.0003, 0.00075 ) ).toVar();

			const hash = Fn( ( [ p ] ) => {

				return fract( sin( dot( p, vec2( 127.1, 311.7 ) ) ).mul( 43758.5453123 ) );

			} );

			const noise = Fn( ( [ p_immutable ] ) => {

				const p = vec2( p_immutable ).toVar();
				const i = floor( p );
				const f = fract( p );
				const ff = f.mul( f ).mul( sub( 3.0, f.mul( 2.0 ) ) );

				const a = hash( i );
				const b = hash( add( i, vec2( 1.0, 0.0 ) ) );
				const c = hash( add( i, vec2( 0.0, 1.0 ) ) );
				const d = hash( add( i, vec2( 1.0, 1.0 ) ) );

				return mix( mix( a, b, ff.x ), mix( c, d, ff.x ), ff.y );

			} );

			const fbm = Fn( ( [ p_immutable ] ) => {

				const p = vec2( p_immutable ).toVar();
				const value = float( 0.0 ).toVar();
				const amplitude = float( 0.5 ).toVar();

				Loop( 5, () => {

					value.addAssign( amplitude.mul( noise( p ) ) );
					p.mulAssign( 2.0 );
					amplitude.mulAssign( 0.5 );

				} );

				return value;

			} );

			If( direction.y.greaterThan( 0.0 ).and( this.cloudCoverage.greaterThan( 0.0 ) ), () => {

				const elevation = mix( 1.0, 0.1, this.cloudElevation );
				const cloudUV = direction.xz.div( direction.y.mul( elevation ) ).toVar();
				cloudUV.mulAssign( this.cloudScale );
				cloudUV.addAssign( time.mul( this.cloudSpeed ) );

				const cloudNoise = fbm( cloudUV.mul( 1000.0 ) ).add( fbm( cloudUV.mul( 2000.0 ).add( 3.7 ) ).mul( 0.5 ) ).toVar();
				cloudNoise.assign( cloudNoise.mul( 0.5 ).add( 0.5 ) );

				const cloudMask = smoothstep( sub( 1.0, this.cloudCoverage ), sub( 1.0, this.cloudCoverage ).add( 0.3 ), cloudNoise ).toVar();

				const horizonFade = smoothstep( 0.0, add( 0.1, mul( 0.2, this.cloudElevation ) ), direction.y );
				cloudMask.mulAssign( horizonFade );

				const sunInfluence = dot( direction, vSunDirection ).mul( 0.5 ).add( 0.5 );
				const daylight = max( 0.0, vSunDirection.y.mul( 2.0 ) );

				const atmosphereColor = Lin.mul( 0.04 );
				const cloudColor = mix( vec3( 0.3 ), vec3( 1.0 ), daylight ).toVar();
				cloudColor.assign( mix( cloudColor, atmosphereColor.add( vec3( 1.0 ) ), sunInfluence.mul( 0.5 ) ) );
				cloudColor.mulAssign( vSunE.mul( 0.00002 ) );

				texColor.assign( mix( texColor, cloudColor, cloudMask.mul( this.cloudDensity ) ) );

			} );

			return vec4( texColor, 1.0 );

		} )();

		material.side = BackSide;
		material.depthWrite = false;

		material.vertexNode = vertexNode;
		material.colorNode = colorNode;

	}

}

export { SkyMesh };
