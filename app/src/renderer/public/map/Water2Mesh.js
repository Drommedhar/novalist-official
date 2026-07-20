import {
	Color,
	Mesh,
	Vector2,
	Vector3,
	NodeMaterial,
	NodeUpdateType,
	TempNode
} from 'three/webgpu';

import { Fn, vec2, viewportSafeUV, viewportSharedTexture, reflector, pow, float, abs, texture, uniform, vec4, cameraPosition, positionWorld, uv, mix, vec3, normalize, max, dot, screenUV, attribute } from 'three/tsl';

/** @module Water2Mesh */

/**
 * An advanced water effect that supports reflections, refractions and flow maps.
 *
 * Note that this class can only be used with {@link WebGPURenderer}.
 * When using {@link WebGLRenderer}, use {@link module:Water2}.
 *
 * References:
 *
 * - {@link https://alex.vlachos.com/graphics/Vlachos-SIGGRAPH10-WaterFlow.pdf}
 * - {@link http://graphicsrunner.blogspot.de/2010/08/water-using-flow-maps.html}
 *
 * @augments Mesh
 * @three_import import { WaterMesh } from 'three/addons/objects/Water2Mesh.js';
 */
class WaterMesh extends Mesh {

	/**
	 * Constructs a new water mesh.
	 *
	 * @param {BufferGeometry} geometry - The water's geometry.
	 * @param {module:Water2~Options} [options] - The configuration options.
	 */
	constructor( geometry, options = {} ) {

		const material = new NodeMaterial();
		material.transparent = true;

		super( geometry, material );

		/**
		 * This flag can be used for type testing.
		 *
		 * @type {boolean}
		 * @readonly
		 * @default true
		 */
		this.isWater = true;

		material.colorNode = new WaterNode( options, this );

	}

}

class WaterNode extends TempNode {

	constructor( options, waterBody ) {

		super( 'vec4' );

		this.waterBody = waterBody;

		this.normalMap0 = texture( options.normalMap0 );
		this.normalMap1 = texture( options.normalMap1 );
		this.flowMap = texture( options.flowMap !== undefined ? options.flowMap : null );

		this.color = uniform( options.color !== undefined ? new Color( options.color ) : new Color( 0xffffff ) );
		this.flowDirection = uniform( options.flowDirection !== undefined ? options.flowDirection : new Vector2( 1, 0 ) );
		this.flowSpeed = uniform( options.flowSpeed !== undefined ? options.flowSpeed : 0.03 );
		this.reflectivity = uniform( options.reflectivity !== undefined ? options.reflectivity : 0.02 );
		this.scale = uniform( options.scale !== undefined ? options.scale : 1 );
		// Relative scale of the second normal layer — kept different from 1 so
		// the two layers don't scroll in lockstep ("sliding texture" look).
		this.scale2 = uniform( options.scale2 !== undefined ? options.scale2 : 1.7 );
		// Ripple-distortion applied to the reflection vs the refraction.
		this._reflectDistort = options.reflectDistort !== undefined ? options.reflectDistort : 1;
		// When true, the refraction sampler is replaced with the flat water
		// colour so the surface output reads as pure reflection-on-opaque
		// (no underlying landscape bleed). Host wrapper layers its own depth
		// tint underneath for the actual water body colour.
		this._reflectionOnly = options.reflectionOnly === true;
		this.flowConfig = uniform( new Vector3() );

		this.updateBeforeType = NodeUpdateType.RENDER;

		this._cycle = 0.15; // a cycle of a flow map phase
		this._halfCycle = this._cycle * 0.5;

		this._USE_FLOW = options.flowMap !== undefined;

	}

	updateFlow( delta ) {

		// Vlachos flow cycle. The scroll offset MUST reset periodically — with
		// per-vertex flow (variable direction + speed taper), an ever-growing
		// offset makes adjacent vertices' texture coords diverge without bound,
		// stretching the ripples apart from the fast centreline outward. The
		// reset keeps that distortion bounded; the cross-fade in setup() hides
		// the reset itself, so it is not visible as a pulse.
		this.flowConfig.value.x += this.flowSpeed.value * delta; // flowMapOffset0
		this.flowConfig.value.y = this.flowConfig.value.x + this._halfCycle; // flowMapOffset1

		if ( this.flowConfig.value.x >= this._cycle ) {

			this.flowConfig.value.x = 0;
			this.flowConfig.value.y = this._halfCycle;

		} else if ( this.flowConfig.value.y >= this._cycle ) {

			this.flowConfig.value.y = this.flowConfig.value.y - this._cycle;

		}

		this.flowConfig.value.z = this._halfCycle;

	}

	updateBefore( frame ) {

		this.updateFlow( frame.deltaTime );

	}

	setup() {

		const outputNode = Fn( () => {

			const flowMapOffset0 = this.flowConfig.x;
			const flowMapOffset1 = this.flowConfig.y;
			const halfCycle = this.flowConfig.z;

			const toEye = normalize( cameraPosition.sub( positionWorld ) );

			let flow;

			if ( this._USE_FLOW === true ) {

				flow = this.flowMap.rg.mul( 2 ).sub( 1 );

			} else {

				// Per-vertex baked flow direction (river centreline tangent /
				// lake drift), set by the host on the geometry's `flowDir`
				// attribute. Falls back to zero if the attribute is absent.
				flow = attribute( 'flowDir', 'vec2' ).toVar();

			}

			flow.x.mulAssign( - 1 );

			// sample normal maps (distort uvs with flowdata)

			const uvs = uv();

			// Vlachos flow: the SAME normal map sampled at two scroll phases,
			// cross-faded. Same map + same scale on both layers means the
			// cross-fade is just one texture blended with itself at a slightly
			// later phase — it never cancels, so there is no "pulse". The
			// cross-fade's job is purely to hide the periodic offset reset.
			const normalUv0 = uvs.mul( this.scale ).add( flow.mul( flowMapOffset0 ) );
			const normalUv1 = uvs.mul( this.scale ).add( flow.mul( flowMapOffset1 ) );

			const normalColor0 = this.normalMap0.sample( normalUv0 );
			const normalColor1 = this.normalMap0.sample( normalUv1 );

			const flowLerp = abs( halfCycle.sub( flowMapOffset0 ) ).div( halfCycle );
			const normalColor = mix( normalColor0, normalColor1, flowLerp );

			// calculate normal vector
			const normal = normalize( vec3( normalColor.r.mul( 2 ).sub( 1 ), normalColor.b, normalColor.g.mul( 2 ).sub( 1 ) ) );

			// calculate the fresnel term to blend reflection and refraction maps
			const theta = max( dot( toEye, normal ), 0 );
			const reflectance = pow( float( 1.0 ).sub( theta ), 5.0 ).mul( float( 1.0 ).sub( this.reflectivity ) ).add( this.reflectivity );

			// reflector, refractor

			const offset = normal.xz.mul( 0.05 ).toVar();

			const reflectionSampler = reflector();
			this.waterBody.add( reflectionSampler.target );
			// The reflection samples a MIRRORED view, so the ripple offset
			// scrolls its ripples opposite to the refraction's. At the fresnel
			// crossover angle both show equally and read as doubled speed —
			// `_reflectDistort` scales the reflection's share of the wobble
			// down (0 = calm reflection, 1 = upstream behaviour).
			reflectionSampler.uvNode = reflectionSampler.uvNode.add( offset.mul( this._reflectDistort ) );

			let refractionSampler;
			if ( this._reflectionOnly ) {

				// Reflection-only mode: refraction "samples" pure water colour
				// instead of the screen behind. The host wrapper supplies the
				// real water tint underneath; this output is reflection layered
				// on top by fresnel.
				refractionSampler = vec4( this.color, 1.0 );

			} else {

				const refractorUV = screenUV.add( offset );
				refractionSampler = viewportSharedTexture( viewportSafeUV( refractorUV ) );

			}

			// calculate final uv coords

			return vec4( this.color, 1.0 ).mul( mix( refractionSampler, reflectionSampler, reflectance ) );

		} )();

		return outputNode;

	}

}

/**
 * Constructor options of `WaterMesh`.
 *
 * @typedef {Object} module:Water2Mesh~Options
 * @property {number|Color|string} [color=0xFFFFFF] - The water color.
 * @property {Vector2} [flowDirection=(1,0)] - The water's flow direction.
 * @property {number} [flowSpeed=0.03] - The water's flow speed.
 * @property {number} [reflectivity=0.02] - The water's reflectivity.
 * @property {number} [scale=1] - The water's scale.
 * @property {?Texture} [flowMap=null] - The flow map. If no flow map is assigned, the water flow is defined by `flowDirection`.
 * @property {Texture} normalMap0 - The first water normal map.
 * @property {Texture} normalMap1 - The second water normal map.
 **/

export { WaterMesh };
